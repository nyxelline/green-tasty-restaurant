using Amazon.DynamoDBv2.DataModel;
using APIHandler.Constants.Orders;
using APIHandler.Converters;
using System.Text.Json.Serialization;

namespace APIHandler.Entities;

public class OrderItem
{
    public const int MaxQuantityPerDish = 99;

    [DynamoDBProperty("dishId")]
    [JsonPropertyName("dishId")]
    public required string DishId { get; set; }

    [DynamoDBProperty("quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [DynamoDBProperty("unitPriceAtOrder")]
    [JsonIgnore]
    public decimal? UnitPriceAtOrder { get; set; }

    [DynamoDBProperty("state", typeof(EnumAsIntConverter<OrderItemState>))]
    [JsonPropertyName("state")]
    public OrderItemState State { get; set; }
}