using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Exceptions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.Reservations;

public class ReservationRepository : IReservationRepository
{
    private static readonly string ReservationsTableName = ResourceNames.ReservationsTableName;
    private readonly IDynamoDBContext _dynamoDbContext;
    private readonly IAmazonDynamoDB _dynamoDbClient;
    private const int MaxLockRetries = 8;
    private static readonly TimeSpan ReservationBuffer = TimeSpan.FromMinutes(15);

    public ReservationRepository(IDynamoDBContext dynamoDbContext, IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
    }

    public async Task<Reservation?> GetReservationByIdAsync(string reservationId)
    {
        return await _dynamoDbContext.LoadAsync<Reservation>(reservationId, DynamoConfig.LoadConfig(ReservationsTableName));
    }

    public async Task<List<Reservation>> GetReservationsByTableIdAsync(string tableId)
    {
        var queryConfig = DynamoConfig.QueryConfig(ReservationsTableName);
        queryConfig.IndexName = ResourceNames.ReservationsTableGlobalIndex;
        queryConfig.ConsistentRead = false;

        return await _dynamoDbContext.QueryAsync<Reservation>(tableId, queryConfig).GetRemainingAsync();
    }

    public async Task<IEnumerable<Reservation>> GetReservationsByWaiterIdAsync(string waiterId)
    {
        var queryConfig = DynamoConfig.QueryConfig(ReservationsTableName);
        queryConfig.IndexName = ResourceNames.ReservationsWaiterIdGlobalIndex;
        queryConfig.ConsistentRead = false;

        return await _dynamoDbContext
            .QueryAsync<Reservation>(waiterId, queryConfig)
            .GetRemainingAsync();
    }

    public async Task<IEnumerable<Reservation>> GetReservationsByCustomerIdAsync(string userId)
    {
        var queryConfig = DynamoConfig.QueryConfig(ReservationsTableName);
        queryConfig.IndexName = ResourceNames.ReservationsCustomerIdGlobalIndex;
        queryConfig.ConsistentRead = false;

        return await _dynamoDbContext
            .QueryAsync<Reservation>(userId, queryConfig)
            .GetRemainingAsync();
    }

    public async Task SaveReservationAsync(Reservation reservation)
    {
        var previousReservation = await GetReservationByIdAsync(reservation.Id);
        if (!await TryWriteReservationAsync(reservation, isCreate: false, previousReservation?.TableId))
            throw new ValidationException("The table slot is no longer available. Refresh availability and choose another time.");
    }

    public Task<bool> TryCreateReservationAsync(Reservation reservation)
        => TryWriteReservationAsync(reservation, isCreate: true, previousTableId: null);

    private async Task<bool> TryWriteReservationAsync(Reservation reservation, bool isCreate, string? previousTableId)
    {
        var tableIds = new[] { reservation.TableId, previousTableId }
            .Where(tableId => !string.IsNullOrWhiteSpace(tableId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tableId => tableId, StringComparer.Ordinal)
            .ToArray();

        for (int attempt = 0; attempt < MaxLockRetries; attempt++)
        {
            var lockStates = new List<TableLockState>(tableIds.Length);
            foreach (var tableId in tableIds)
                lockStates.Add(await GetTableLockStateAsync(tableId));

            var destinationLock = lockStates.First(state =>
                string.Equals(state.TableId, reservation.TableId, StringComparison.OrdinalIgnoreCase));

                if (IsActiveReservation(reservation) && destinationLock.Reservations.Any(existing =>
                    existing.EndTime > DateTime.UtcNow &&
                    !string.Equals(existing.Id, reservation.Id, StringComparison.OrdinalIgnoreCase) &&
                    IsOverlapping(existing, reservation)))
                return false;

            var transactionItems = new List<TransactWriteItem>(lockStates.Count + 1);
            foreach (var state in lockStates)
            {
                var nextReservations = state.Reservations
                    .Where(existing =>
                        !string.Equals(existing.Id, reservation.Id, StringComparison.OrdinalIgnoreCase) &&
                        existing.EndTime > DateTime.UtcNow)
                    .ToList();

                if (IsActiveReservation(reservation) &&
                    string.Equals(state.TableId, reservation.TableId, StringComparison.OrdinalIgnoreCase))
                    nextReservations.Add(ToLockedReservation(reservation));

                var lockPut = new Put
                {
                    TableName = ReservationsTableName,
                    Item = BuildLockItem(state.LockId, state.Version + 1, nextReservations),
                    ConditionExpression = state.Exists ? "lockVersion = :version" : "attribute_not_exists(id)"
                };

                if (state.Exists)
                {
                    lockPut.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":version"] = new() { N = state.Version.ToString(CultureInfo.InvariantCulture) }
                    };
                }

                transactionItems.Add(new TransactWriteItem { Put = lockPut });
            }

            var reservationDocument = _dynamoDbContext.ToDocument(reservation).ToAttributeMap();
            var reservationPut = new Put
            {
                TableName = ReservationsTableName,
                Item = reservationDocument
            };

            if (isCreate)
                reservationPut.ConditionExpression = "attribute_not_exists(id)";

            transactionItems.Add(new TransactWriteItem { Put = reservationPut });

            try
            {
                await _dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
                {
                    TransactItems = transactionItems
                });

                return true;
            }
            catch (TransactionCanceledException)
            {
                if (isCreate && await _dynamoDbContext.LoadAsync<Reservation>(
                        reservation.Id,
                        DynamoConfig.LoadConfig(ReservationsTableName)) is not null)
                    return false;
            }
        }

        return false;
    }

    private async Task<TableLockState> GetTableLockStateAsync(string tableId)
    {
        string lockId = GetLockId(tableId);
        var lockResponse = await _dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = ReservationsTableName,
            Key = new Dictionary<string, AttributeValue> { ["id"] = new() { S = lockId } },
            ConsistentRead = true
        });

        if (lockResponse.Item is { Count: > 0 })
        {
            return new TableLockState(
                tableId,
                lockId,
                true,
                long.Parse(lockResponse.Item["lockVersion"].N, CultureInfo.InvariantCulture),
                ReadLockedReservations(lockResponse.Item));
        }

        var existingReservations = await ScanReservationsForTableAsync(tableId);
        return new TableLockState(
            tableId,
            lockId,
            false,
            0,
            existingReservations.Where(IsActiveReservation).Select(ToLockedReservation).ToList());
    }

    private async Task<List<Reservation>> ScanReservationsForTableAsync(string tableId)
    {
        var results = await _dynamoDbContext.ScanAsync<Reservation>([], new ScanConfig
        {
            OverrideTableName = ReservationsTableName,
            ConsistentRead = true
        }).GetRemainingAsync();

        return results.Where(reservation =>
            string.Equals(reservation.TableId, tableId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool IsActiveReservation(Reservation reservation)
        => reservation.Status is ReservationStatus.Reserved or ReservationStatus.InProgress;

    private static bool IsOverlapping(LockedReservation existing, Reservation candidate)
        => candidate.StartTime.ToUniversalTime() < existing.EndTime.Add(ReservationBuffer) &&
           candidate.EndTime.ToUniversalTime() > existing.StartTime.Subtract(ReservationBuffer);

    private static LockedReservation ToLockedReservation(Reservation reservation)
        => new(
            reservation.Id,
            reservation.StartTime.ToUniversalTime(),
            reservation.EndTime.ToUniversalTime());

    private static List<LockedReservation> ReadLockedReservations(Dictionary<string, AttributeValue> item)
    {
        if (!item.TryGetValue("activeReservations", out var reservationsAttribute) || reservationsAttribute.L is null)
            return [];

        return reservationsAttribute.L.Select(entry => new LockedReservation(
            entry.M["reservationId"].S,
            new DateTime(long.Parse(entry.M["startTicks"].N, CultureInfo.InvariantCulture), DateTimeKind.Utc),
            new DateTime(long.Parse(entry.M["endTicks"].N, CultureInfo.InvariantCulture), DateTimeKind.Utc)))
            .ToList();
    }

    private static Dictionary<string, AttributeValue> BuildLockItem(
        string lockId,
        long version,
        IEnumerable<LockedReservation> reservations)
        => new()
        {
            ["id"] = new() { S = lockId },
            ["lockVersion"] = new() { N = version.ToString(CultureInfo.InvariantCulture) },
            ["activeReservations"] = new()
            {
                L = reservations.Select(reservation => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["reservationId"] = new() { S = reservation.Id },
                        ["startTicks"] = new() { N = reservation.StartTime.Ticks.ToString(CultureInfo.InvariantCulture) },
                        ["endTicks"] = new() { N = reservation.EndTime.Ticks.ToString(CultureInfo.InvariantCulture) }
                    }
                }).ToList()
            }
        };

    private static string GetLockId(string tableId) => $"__table_reservation_lock__:{tableId}";

    private sealed record LockedReservation(string Id, DateTime StartTime, DateTime EndTime);
    private sealed record TableLockState(
        string TableId,
        string LockId,
        bool Exists,
        long Version,
        List<LockedReservation> Reservations);
}