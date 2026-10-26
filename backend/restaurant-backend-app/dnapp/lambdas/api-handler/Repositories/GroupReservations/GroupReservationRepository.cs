using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using System.Collections.Generic;
using System.Globalization;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GroupReservations;

public class GroupReservationRepository : IGroupReservationRepository
{
    private static readonly string GroupReservationsTableName = ResourceNames.GroupReservationsTableName;
    private readonly IDynamoDBContext _dynamoDbContext;
    private readonly IAmazonDynamoDB _dynamoDbClient;

    public GroupReservationRepository(IDynamoDBContext dynamoDbContext, IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
    }

    public async Task<GroupReservation?> GetGroupReservationByIdAsync(string groupReservationId)
    {
        return await _dynamoDbContext.LoadAsync<GroupReservation>(groupReservationId, DynamoConfig.LoadConfig(GroupReservationsTableName));
    }

    public async Task DeleteGroupReservationAsync(GroupReservation groupReservation)
    {
        await _dynamoDbContext.DeleteAsync(groupReservation, DynamoConfig.DeleteConfig(GroupReservationsTableName));
    }

    public async Task SaveGroupReservationAsync(GroupReservation groupReservation)
    {
        await _dynamoDbContext.SaveAsync(groupReservation, DynamoConfig.SaveConfig(GroupReservationsTableName));
    }

    public async Task<bool> TryCreateGroupReservationAsync(GroupReservation groupReservation)
    {
        try
        {
            await _dynamoDbClient.PutItemAsync(new PutItemRequest
            {
                TableName = GroupReservationsTableName,
                ConditionExpression = "attribute_not_exists(groupReservationId)",
                Item = new Dictionary<string, AttributeValue>
                {
                    ["groupReservationId"] = new() { S = groupReservation.GroupReservationId },
                    ["reservationId"] = new() { S = groupReservation.ReservationId },
                    ["reservationToken"] = new() { S = groupReservation.ReservationToken },
                    ["status"] = new() { S = groupReservation.Status.ToString() },
                    ["joinedGuestsCount"] = new() { N = groupReservation.JoinedGuestsCount.ToString(CultureInfo.InvariantCulture) }
                }
            });

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<GroupReservation?> GetGroupReservationByTokenAsync(string token)
    {
        var results = await _dynamoDbContext.QueryAsync<GroupReservation?>(token, new QueryConfig
        {
            OverrideTableName = GroupReservationsTableName,
            IndexName = GlobalSecondaryIndexes.GroupReservation.ReservationToken,
        }).GetRemainingAsync();

        return results.FirstOrDefault();
    }

    public async Task<GroupReservation?> GetByReservationIdAsync(string reservationId)
    {
        var results = await _dynamoDbContext.QueryAsync<GroupReservation?>(reservationId, new QueryConfig
        {
            OverrideTableName = GroupReservationsTableName,
            IndexName = GlobalSecondaryIndexes.GroupReservation.ReservationId,
        }).GetRemainingAsync();

        return results.FirstOrDefault();
    }
}
