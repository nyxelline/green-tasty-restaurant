using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

public class BillLineResponse
{
    [JsonPropertyName("dishId")]
    public required string DishId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("lineTotal")]
    public decimal LineTotal { get; set; }
}

public class GuestBillResponse
{
    [JsonPropertyName("guestSessionId")]
    public string? GuestSessionId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("isOwner")]
    public bool IsOwner { get; set; }

    [JsonPropertyName("isSubmitted")]
    public bool IsSubmitted { get; set; }

    [JsonPropertyName("lines")]
    public required IReadOnlyList<BillLineResponse> Lines { get; set; }

    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    /// Share of the whole group's bill, in percent (one decimal place).
    [JsonPropertyName("sharePercent")]
    public decimal SharePercent { get; set; }
}

public class GroupBillResponse
{
    [JsonPropertyName("groupReservationId")]
    public required string GroupReservationId { get; set; }

    [JsonPropertyName("guests")]
    public required IReadOnlyList<GuestBillResponse> Guests { get; set; }

    [JsonPropertyName("total")]
    public decimal Total { get; set; }
}
