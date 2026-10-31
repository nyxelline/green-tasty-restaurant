using APIHandler.Constants;
using APIHandler.Models.Orders;
using System;
using System.Text.Json.Serialization;

namespace APIHandler.Models.Reservations;

public class ReservationResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("tableId")]
    public required string TableId { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }

    [JsonPropertyName("guestCount")]
    public int GuestCount { get; set; }

    [JsonPropertyName("status")]
    public ReservationStatus Status { get; set; }

    [JsonPropertyName("customerId")]
    public string? CustomerId { get; set; }

    [JsonPropertyName("waiterId")]
    public required string WaiterId { get; set; }

    [JsonPropertyName("locationAddress")]
    public required string Address { get; set; }

    [JsonPropertyName("restaurantName")]
    public required string RestaurantName { get; set; } = "Green & Tasty";
    
    [JsonPropertyName("hasSubmittedPreOrder")]
    public bool HasSubmittedPreOrder { get; set; }

    [JsonPropertyName("guestInviteLinkAvailable")]
    public bool GuestInviteLinkAvailable { get; set; }

    // The reservation's own (non-cancelled) order, if any — lets the reservations list show
    // the real dish count instead of always reading as empty.
    [JsonPropertyName("preOrder")]
    public GetOrderResponse? PreOrder { get; set; }
}