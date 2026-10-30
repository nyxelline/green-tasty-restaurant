using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RestaurantAqa.Tests.Api;

// NOTE on casing below: the backend's ResponseBuilder.CreateSuccessResponse(object) serializes
// with System.Text.Json's *default* options (no camelCase policy), so any response DTO on the
// backend that has no [JsonPropertyName] attributes of its own comes back over the wire using its
// exact C# property names (PascalCase). ResponseBuilder.CreateJsonResponse(...) is the one that
// forces camelCase. Each DTO below is cased to match whichever path its real backend handler uses
// (verified by reading the handler + response model source, not guessed).

/// SignInHandler -> CreateSuccessResponse(SignInResponse) -- SignInResponse itself has explicit
/// [JsonPropertyName] attributes (camelCase), so this one stays camelCase either way.
public record SignInResponseDto(
    [property: JsonPropertyName("idToken")] string? IdToken,
    [property: JsonPropertyName("accessToken")] string? AccessToken);

/// GetAvailableTablesHandler -> CreateJsonResponse(Table[]) -- Table entity has its own
/// [JsonPropertyName] attributes, so this is "id"/"capacity" either way.
public record AvailableTableDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("capacity")] int Capacity);

/// GetDishesByLocationIdHandler / GetAllDishesHandler -> dish entities also carry their own
/// explicit [JsonPropertyName] attributes.
public record DishSummaryDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("state")] string? State);

/// CreateReservationHandler -> CreateSuccessResponse(ReservationResponse) -- ReservationResponse
/// has explicit [JsonPropertyName] attributes (camelCase).
public record CreateReservationResponseDto(
    [property: JsonPropertyName("id")] string Id);

/// GetInviteTokenHandler -> CreateJsonResponse(GetGroupInviteTokenResponse) -- that response record
/// has NO attributes of its own, so the handler's CamelCase policy applies: "token"/"groupReservationId".
public record InviteTokenDto(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("groupReservationId")] string? GroupReservationId);

/// CreateGuestSessionHandler -> CreateSuccessResponse(CreatGuestSessionResponse) -- that response
/// record (SessionId, SessionToken) has NO attributes, and this path does NOT apply camelCase,
/// so the real wire shape is PascalCase: "SessionId"/"SessionToken".
public record CreateGuestSessionResponseDto(
    [property: JsonPropertyName("SessionId")] string SessionId,
    [property: JsonPropertyName("SessionToken")] string SessionToken);

/// GetGroupSessionsHandler -> CreateSuccessResponse(GetAllSessionsResponse) -- that response record
/// (Participants, Total) and the nested Participant record have NO attributes either, so the real
/// wire shape is PascalCase all the way down: "Participants"/"Total", and per participant
/// "Id"/"Type"/"Name"/"Items"/"Total"/"IsSubmitted".
public record ParticipantDto(
    [property: JsonPropertyName("Id")] string? Id,
    [property: JsonPropertyName("Type")] string Type,
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Items")] List<object>? Items,
    [property: JsonPropertyName("Total")] decimal Total,
    [property: JsonPropertyName("IsSubmitted")] bool IsSubmitted);

public record GroupSessionsResponseDto(
    [property: JsonPropertyName("Participants")] List<ParticipantDto> Participants,
    [property: JsonPropertyName("Total")] decimal Total);
