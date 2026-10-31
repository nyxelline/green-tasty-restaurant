using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Repositories.Reservations;
using Moq;
using System.Globalization;

namespace APIHandler.Tests.Repositories;

public class ReservationRepositoryTests
{
    private readonly Mock<IDynamoDBContext> _context = new();
    private readonly Mock<IAmazonDynamoDB> _client = new();
    private readonly ReservationRepository _repository;

    public ReservationRepositoryTests()
    {
        Environment.SetEnvironmentVariable("reservationsTable", "test-reservations");
        _context
            .Setup(context => context.ToDocument(It.IsAny<Reservation>()))
            .Returns((Reservation reservation) => new Document
            {
                ["id"] = reservation.Id,
                ["tableId"] = reservation.TableId,
                ["waiterId"] = reservation.WaiterId,
                ["status"] = reservation.Status.ToString()
            });

        _repository = new ReservationRepository(_context.Object, _client.Object);
    }

    [Fact]
    public async Task TryCreateReservationAsync_OverlappingLockEntry_RejectsCreate()
    {
        var startTime = DateTime.UtcNow.Date.AddDays(1).AddHours(12);
        SetupLockResponse(1, [LockedReservation("existing", startTime.AddMinutes(30), startTime.AddMinutes(120))]);

        bool created = await _repository.TryCreateReservationAsync(ReservationFor("candidate", startTime, startTime.AddMinutes(90)));

        Assert.False(created);
        _client.Verify(client => client.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryCreateReservationAsync_FreeSlot_WritesLockAndReservationTogether()
    {
        var startTime = DateTime.UtcNow.Date.AddDays(1).AddHours(12);
        SetupLockResponse(3, [LockedReservation("other", startTime.AddHours(3), startTime.AddHours(4))]);

        TransactWriteItemsRequest? transaction = null;
        _client
            .Setup(client => client.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((request, _) => transaction = request)
            .ReturnsAsync(new TransactWriteItemsResponse());

        bool created = await _repository.TryCreateReservationAsync(ReservationFor("candidate", startTime, startTime.AddMinutes(90)));

        Assert.True(created);
        Assert.Equal(2, transaction!.TransactItems.Count);
        Assert.Contains(transaction.TransactItems, item => item.Put?.Item.ContainsKey("lockVersion") == true);
        Assert.Contains(transaction.TransactItems, item => item.Put?.Item.ContainsKey("tableId") == true);
    }

    [Fact]
    public async Task TryCreateReservationAsync_CompareAndSwapConflict_RereadsLockAndRejectsOverlap()
    {
        var startTime = DateTime.UtcNow.Date.AddDays(1).AddHours(12);
        _client
            .SetupSequence(client => client.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LockResponse(4, []))
            .ReturnsAsync(LockResponse(5, [LockedReservation("winner", startTime.AddMinutes(15), startTime.AddMinutes(90))]));
        _client
            .Setup(client => client.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("Lock version changed"));

        bool created = await _repository.TryCreateReservationAsync(ReservationFor("candidate", startTime, startTime.AddMinutes(60)));

        Assert.False(created);
        _client.Verify(client => client.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _client.Verify(client => client.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupLockResponse(long version, List<AttributeValue> activeReservations)
        => _client
            .Setup(client => client.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LockResponse(version, activeReservations));

    private static GetItemResponse LockResponse(long version, List<AttributeValue> activeReservations)
        => new()
        {
            Item = new Dictionary<string, AttributeValue>
            {
                ["id"] = new() { S = "__table_reservation_lock__:table-1" },
                ["lockVersion"] = new() { N = version.ToString(CultureInfo.InvariantCulture) },
                ["activeReservations"] = new() { L = activeReservations }
            }
        };

    private static AttributeValue LockedReservation(string id, DateTime startTime, DateTime endTime)
        => new()
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["reservationId"] = new() { S = id },
                ["startTicks"] = new() { N = startTime.Ticks.ToString(CultureInfo.InvariantCulture) },
                ["endTicks"] = new() { N = endTime.Ticks.ToString(CultureInfo.InvariantCulture) }
            }
        };

    private static Reservation ReservationFor(string id, DateTime startTime, DateTime endTime)
        => new()
        {
            Id = id,
            TableId = "table-1",
            WaiterId = "waiter-1",
            CustomerId = "customer-1",
            StartTime = startTime,
            EndTime = endTime,
            GuestCount = 2,
            Status = ReservationStatus.Reserved
        };
}