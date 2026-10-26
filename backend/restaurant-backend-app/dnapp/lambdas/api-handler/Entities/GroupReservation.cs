using Amazon.DynamoDBv2.DataModel;

namespace APIHandler.Entities;

public class GroupReservation
{
    [DynamoDBHashKey("groupReservationId")]
    public required string GroupReservationId { get; set; }

    [DynamoDBProperty("reservationId")]
    [DynamoDBGlobalSecondaryIndexHashKey("reservationId")]
    public required string ReservationId { get; set; }

    [DynamoDBProperty("reservationToken")]
    [DynamoDBGlobalSecondaryIndexHashKey("reservationToken")]
    public required string ReservationToken { get; set; }

    [DynamoDBProperty("status")]
    public GroupReservationStatus Status { get; set; }

    // Atomically incremented under a conditional UpdateItem when a guest joins, so two guests
    // joining at the same moment can't both slip past the GuestCount limit (DynamoDB
    // read-then-write races were otherwise possible here).
    [DynamoDBProperty("joinedGuestsCount")]
    public int JoinedGuestsCount { get; set; }
}