using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

public record SubmitGroupPreOrderRequest(
    [property: Required] [property: JsonPropertyName("reservationId")] string ReservationId);

public record MergedDishResponse(
    [property: JsonPropertyName("dishId")] string DishId,
    [property: JsonPropertyName("quantity")] int Quantity);
