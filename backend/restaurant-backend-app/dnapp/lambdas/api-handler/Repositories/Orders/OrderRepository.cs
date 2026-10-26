using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.Orders;

public class OrderRepository : IOrderRepository
{
    private const string ReservationLockCustomerId = "__reservation_order_lock__";

    private readonly string OrdersTableName = ResourceNames.OrdersTableName;
    private readonly string CustomerIdGlobalIndex = ResourceNames.OrdersTableCustomerIdGlobalIndex;
    private readonly string ReservationIdGlobalIndex = ResourceNames.OrdersTableReservationIdGlobalIndex;

    private readonly IDynamoDBContext _dynamoDbContext;
    private readonly IAmazonDynamoDB _dynamoDbClient;

    public OrderRepository(IDynamoDBContext dynamoDbContext, IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
    }

    public async Task<Order?> GetOrderByIdAsync(string orderId, string customerId)
    {
        return await _dynamoDbContext.LoadAsync<Order>(orderId, customerId, DynamoConfig.LoadConfig(OrdersTableName));
    }

    public async Task<IEnumerable<Order>> GetOrdersByCustomerIdAsync(string customerId)
    {
        var config = new QueryConfig
        {
            OverrideTableName = OrdersTableName,
            IndexName = CustomerIdGlobalIndex,
        };

        var query = _dynamoDbContext.QueryAsync<Order>(customerId, config);

        return await query.GetRemainingAsync();
    }

    public async Task<Order?> GetOrderByReservationIdAsync(string reservationId)
    {
        var config = new QueryConfig
        {
            OverrideTableName = OrdersTableName,
            IndexName = ReservationIdGlobalIndex,
        };

        var results = await _dynamoDbContext.QueryAsync<Order>(reservationId, config).GetRemainingAsync();

        return results.FirstOrDefault(o => o.Status != OrderStatus.Cancelled);
    }

    public async Task SaveOrderAsync(Order order)
    {
        if (order.Status != OrderStatus.Cancelled)
        {
            await _dynamoDbContext.SaveAsync(order, DynamoConfig.SaveConfig(OrdersTableName));
            return;
        }

        await _dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = new List<TransactWriteItem>
            {
                new()
                {
                    Put = new Put
                    {
                        TableName = OrdersTableName,
                        Item = ToDynamoOrderItem(order)
                    }
                },
                new()
                {
                    Update = new Update
                    {
                        TableName = OrdersTableName,
                        Key = ReservationLockKey(order.ReservationId),
                        UpdateExpression = "SET #status = :cancelled",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":cancelled"] = new() { N = ((int)OrderStatus.Cancelled).ToString(CultureInfo.InvariantCulture) }
                        }
                    }
                }
            }
        });
    }

    public async Task<bool> TryCreateOrderAsync(Order order)
    {
        try
        {
            await _dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems = new List<TransactWriteItem>
                {
                    new()
                    {
                        Put = new Put
                        {
                            TableName = OrdersTableName,
                            Item = ReservationLockItem(order.ReservationId, (int)order.Status),
                            ConditionExpression = "attribute_not_exists(orderId) OR #status = :cancelled",
                            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "status" },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":cancelled"] = new() { N = ((int)OrderStatus.Cancelled).ToString(CultureInfo.InvariantCulture) }
                            }
                        }
                    },
                    new()
                    {
                        Put = new Put
                        {
                            TableName = OrdersTableName,
                            Item = ToDynamoOrderItem(order)
                        }
                    }
                },
            });

            return true;
        }
        catch (TransactionCanceledException)
        {
            return false;
        }
    }

    private static Dictionary<string, AttributeValue> ToDynamoOrderItem(Order order)
    {
        var dishes = order.Dishes.Select(dish =>
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["dishId"] = new() { S = dish.DishId },
                ["quantity"] = new() { N = dish.Quantity.ToString(CultureInfo.InvariantCulture) },
                ["state"] = new() { N = ((int)dish.State).ToString(CultureInfo.InvariantCulture) }
            };

            if (dish.UnitPriceAtOrder.HasValue)
                item["unitPriceAtOrder"] = new AttributeValue { N = dish.UnitPriceAtOrder.Value.ToString(CultureInfo.InvariantCulture) };

            return new AttributeValue { M = item };
        }).ToList();

        return new Dictionary<string, AttributeValue>
        {
            ["orderId"] = new() { S = order.OrderId },
            ["customerId"] = new() { S = order.CustomerId },
            ["reservationId"] = new() { S = order.ReservationId },
            ["reservationAddress"] = new() { S = order.ReservationAddress },
            ["reservationTime"] = new() { S = order.ReservationTime },
            ["dishes"] = new() { L = dishes },
            ["status"] = new() { N = ((int)order.Status).ToString(CultureInfo.InvariantCulture) }
        };
    }

    private static Dictionary<string, AttributeValue> ReservationLockKey(string reservationId) => new()
    {
        ["orderId"] = new AttributeValue { S = reservationId },
        ["customerId"] = new AttributeValue { S = ReservationLockCustomerId }
    };

    private static Dictionary<string, AttributeValue> ReservationLockItem(string reservationId, int status)
    {
        var item = ReservationLockKey(reservationId);
        item["status"] = new AttributeValue { N = status.ToString(CultureInfo.InvariantCulture) };
        return item;
    }
}