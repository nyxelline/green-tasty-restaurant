using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Mappers;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Services.Auth;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using APIHandler.Services.Notifications;

namespace APIHandler.Services.GuestSessions;

public class GuestSessionService(
    IGroupReservationRepository groupReservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IDishRepository dishRepository,
    IReservationRepository reservationRepository,
    IUserRepository userRepository,
    ITableRepository tableRepository,
    IOrderRepository orderRepository,
    IUserService userService,
    IAmazonDynamoDB dynamoDbClient) : IGuestSessionService
{
    public async Task<bool> IsAuthorizedAsync(string guestSessionToken, string guestSessionId)
    {
        string hash = Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(guestSessionToken)));
        GuestSession? guestSession = await guestSessionRepository.GetByTokenHash(hash);
        if (guestSession is null)
            return false;

        return guestSession.Id == guestSessionId;
    }

    public async Task<CreatGuestSessionResponse> CreateGuestSessionAsync(CreateGuestSessionRequest request)
    {
        GroupReservation group = await GetGroupReservationAndValidateAsync(request.GroupReservationId);

        Reservation reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new GroupReservationExpiredException($"Associated reservation for group '{request.GroupReservationId}' not found.");

        if (reservation.Status != ReservationStatus.Reserved)
        {
            throw new GuestSessionExpiredException($"This pre-order session has expired or is no longer active. Current reservation status: '{reservation.Status}'.");
        }

        await EnsureGuestCounterInitializedAsync(group.GroupReservationId);

        // Two guests can legitimately share a first name, and the same person may rejoin under
        // the same name after losing their session — so a collision is disambiguated with a
        // suffix rather than rejected outright. Without this, the owner's guest list (and the
        // "added to X's table" message) can't tell two same-named guests apart. The owner's
        // display name is checked too: her own GuestSession record is tagged "Owner", not her
        // real name, so without this a guest naming themselves after the owner would collide
        // unnoticed in the participants list and bill.
        string? ownerDisplayName = string.IsNullOrWhiteSpace(reservation.CustomerId)
            ? null
            : await userService.GetUserName(reservation.CustomerId);
        string guestName = await ResolveUniqueGuestNameAsync(group.GroupReservationId, request.GuestName, ownerDisplayName);

        int maxJoinedGuests = reservation.GuestCount - 1;
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        var session = new GuestSession
        {
            Id = Guid.NewGuid().ToString(),
            GuestName = guestName,
            GroupReservationId = group.GroupReservationId,
            SessionTokenHash = Convert.ToBase64String(SHA256.HashData(tokenBytes)),
            Dishes = [],
        };

        try
        {
            await dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems = new List<TransactWriteItem>
                {
                    new()
                    {
                        Update = new Update
                        {
                            TableName = ResourceNames.GroupReservationsTableName,
                            Key = new Dictionary<string, AttributeValue>
                            {
                                ["groupReservationId"] = new AttributeValue { S = group.GroupReservationId }
                            },
                            UpdateExpression = "SET joinedGuestsCount = if_not_exists(joinedGuestsCount, :zero) + :incr",
                            ConditionExpression = "(attribute_not_exists(joinedGuestsCount) AND :zero < :max) OR joinedGuestsCount < :max",
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":zero"] = new AttributeValue { N = "0" },
                                [":incr"] = new AttributeValue { N = "1" },
                                [":max"] = new AttributeValue { N = maxJoinedGuests.ToString() }
                            }
                        }
                    },
                    new()
                    {
                        Put = new Put
                        {
                            TableName = ResourceNames.GuestSessionTableName,
                            Item = new Dictionary<string, AttributeValue>
                            {
                                ["guestSessionId"] = new AttributeValue { S = session.Id },
                                ["groupReservationId"] = new AttributeValue { S = session.GroupReservationId },
                                ["guestName"] = new AttributeValue { S = session.GuestName },
                                ["guestSessionTokenHash"] = new AttributeValue { S = session.SessionTokenHash },
                                ["dishes"] = new AttributeValue { L = [] },
                                ["isOwnerSession"] = new AttributeValue { BOOL = false },
                                ["isSubmitted"] = new AttributeValue { BOOL = false }
                            },
                            ConditionExpression = "attribute_not_exists(guestSessionId)"
                        }
                    }
                }
            });
        }
        catch (TransactionCanceledException)
        {
            var groupRecord = await dynamoDbClient.GetItemAsync(new GetItemRequest
            {
                TableName = ResourceNames.GroupReservationsTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["groupReservationId"] = new AttributeValue { S = group.GroupReservationId }
                },
                ConsistentRead = true,
                ProjectionExpression = "joinedGuestsCount"
            });

            int joinedGuestsCount = groupRecord.Item is not null &&
                groupRecord.Item.TryGetValue("joinedGuestsCount", out var currentCount) &&
                int.TryParse(currentCount.N, out var parsedCount)
                    ? parsedCount
                    : 0;

            if (joinedGuestsCount >= maxJoinedGuests)
                throw new GuestLimitExceededException($"Guest limit exceeded. This reservation is for {reservation.GuestCount} people: the owner plus {maxJoinedGuests} guests, and {joinedGuestsCount} guests have already joined. Ask the owner to raise the guest count to invite more.");

            throw;
        }

        return new CreatGuestSessionResponse(session.Id, Convert.ToBase64String(tokenBytes));
    }

    public async Task<GuestMenuResponse> GetGuestMenuAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new ValidationException($"Guest session '{guestSessionId}' not found.");

        await ValidateGroupReservationId(session.GroupReservationId);

        string locationId = await GetLocationIdForGroupAsync(session.GroupReservationId);
        var dishes = await dishRepository.GetDishesByLocationIdAsync(locationId);

        var dishesMapped = dishes
            .GroupBy(d => d.DishType)
            .ToDictionary(k => k.Key, GuestMenuDishMapper.Map);

        return new GuestMenuResponse(dishesMapped);
    }

    public async Task AddDishAsync(string guestSessionId, string dishId, int quantity)
    {
        if (quantity <= 0 || quantity > OrderItem.MaxQuantityPerDish)
            throw new ValidationException($"Quantity must be between 1 and {OrderItem.MaxQuantityPerDish}.");

        GuestSession session = await GetGuestSessionAsync(guestSessionId);

        string locationId = await GetLocationIdForGroupAsync(session.GroupReservationId);
        var locationDishes = await dishRepository.GetDishesByLocationIdAsync(locationId);

        var dish = locationDishes.FirstOrDefault(d => d.Id == dishId)
            ?? throw new ValidationException($"Dish '{dishId}' does not exist or is not available at this location.");

        if (dish.State != DishState.Available)
            throw new DishIsNotAvailableException($"Dish '{dishId}' is currently unavailable. State: '{dish.State}'.");

        session.Dishes ??= [];

        var existingDish = session.Dishes.FirstOrDefault(d => d.DishId == dishId);

        if (existingDish != null)
        {
            if (existingDish.Quantity > OrderItem.MaxQuantityPerDish - quantity)
                throw new ValidationException($"A maximum of {OrderItem.MaxQuantityPerDish} of one dish can be ordered.");

            existingDish.Quantity += quantity;
        }
        else
        {
            session.Dishes.Add(new OrderItem
            {
                DishId = dishId,
                Quantity = quantity,
                UnitPriceAtOrder = dish.Price,
                State = OrderItemState.Pending
            });
        }

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task RemoveDishAsync(string guestSessionId, string dishId, int quantity)
    {
        if (quantity <= 0)
            throw new ValidationException("Quantity must be greater than zero.");

        GuestSession session = await GetGuestSessionAsync(guestSessionId);

        if (session.Dishes == null || session.Dishes.Count == 0)
            throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        var existingDish = session.Dishes.FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        if (quantity >= existingDish.Quantity)
        {
            session.Dishes.Remove(existingDish);
        }
        else
        {
            existingDish.Quantity -= quantity;
        }

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task<GroupReservationStatusResponse> GetGroupReservationStatusAsync(string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new InvalidOperationException("Associated reservation not found.");

        int currentParticipants = await GetCurrentParticipantCountAsync(groupReservationId);

        return new GroupReservationStatusResponse
        {
            CurrentParticipants = currentParticipants,
            MaxCapacity = reservation.GuestCount,
            Status = group.Status
        };
    }

    
    public async Task<GetAllSessionsResponse> DeleteGuestSessionAsync(string userId, string groupReservationId, string guestSessionId)
    {
        await ValidateReservationOwnerAsync(groupReservationId, userId);

        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session with id {guestSessionId} not found.");

        if (session.GroupReservationId != groupReservationId)
        {
            throw new ValidationException("Guest session does not belong to this group reservation.");
        }

        await GetGroupReservationAndValidateAsync(groupReservationId);

        if (session.IsOwnerSession)
        {
            await guestSessionRepository.DeleteAsync(session);
        }
        else
        {
            try
            {
                await dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
                {
                    TransactItems = new List<TransactWriteItem>
                    {
                        new()
                        {
                            Delete = new Delete
                            {
                                TableName = ResourceNames.GuestSessionTableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["guestSessionId"] = new AttributeValue { S = session.Id }
                                },
                                ConditionExpression = "groupReservationId = :groupId",
                                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                                {
                                    [":groupId"] = new AttributeValue { S = groupReservationId }
                                }
                            }
                        },
                        new()
                        {
                            Update = new Update
                            {
                                TableName = ResourceNames.GroupReservationsTableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["groupReservationId"] = new AttributeValue { S = groupReservationId }
                                },
                                UpdateExpression = "SET joinedGuestsCount = joinedGuestsCount - :decr",
                                ConditionExpression = "attribute_exists(joinedGuestsCount) AND joinedGuestsCount > :zero",
                                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                                {
                                    [":zero"] = new AttributeValue { N = "0" },
                                    [":decr"] = new AttributeValue { N = "1" }
                                }
                            }
                        }
                    }
                });
            }
            catch (TransactionCanceledException)
            {
                var groupRecord = await dynamoDbClient.GetItemAsync(new GetItemRequest
                {
                    TableName = ResourceNames.GroupReservationsTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["groupReservationId"] = new AttributeValue { S = groupReservationId }
                    },
                    ConsistentRead = true,
                    ProjectionExpression = "joinedGuestsCount"
                });

                if (groupRecord.Item is not null &&
                    groupRecord.Item.TryGetValue("joinedGuestsCount", out var currentCount) &&
                    int.TryParse(currentCount.N, out int joinedGuestsCount) && joinedGuestsCount > 0)
                {
                    throw;
                }

                await guestSessionRepository.DeleteAsync(session);
            }
        }

        return await BuildSessionsSnapshotAsync(groupReservationId);
    }

    public async Task<GetAllSessionsResponse> GetGroupSessionsAsync(string userId, string groupReservationId)
    {
        await ValidateReservationOwnerAsync(groupReservationId, userId);

        return await BuildSessionsSnapshotAsync(groupReservationId);
    }

    public async Task<GroupBillResponse> GetGroupBillAsync(string userId, string groupReservationId)
    {
        await ValidateReservationOwnerAsync(groupReservationId, userId);

        var snapshot = await BuildSessionsSnapshotAsync(groupReservationId);
        return BillCalculator.Build(groupReservationId, snapshot.Participants);
    }

    public async Task<GuestBillResponse> GetGuestBillAsync(string guestSessionId)
    {
        // Read directly: a submitted guest must still be able to see his or her share.
        GuestSession guestSession = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session {guestSessionId} not found.");

        var snapshot = await BuildSessionsSnapshotAsync(guestSession.GroupReservationId);
        var bill = BillCalculator.Build(guestSession.GroupReservationId, snapshot.Participants);

        return bill.Guests.FirstOrDefault(guest => guest.GuestSessionId == guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session with id {guestSessionId} not found.");
    }

    public async Task<GetAllSessionsResponse> RemoveDishAsOwnerAsync(string userId, string groupReservationId, string guestSessionId, string dishId)
    {
        await ValidateReservationOwnerAsync(groupReservationId, userId);
        await GetGroupReservationAndValidateAsync(groupReservationId);

        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session with id {guestSessionId} not found.");

        if (session.GroupReservationId != groupReservationId)
        {
            throw new ValidationException("Guest session does not belong to this group reservation.");
        }

        var removedItem = session.Dishes?.FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        session.Dishes!.Remove(removedItem);

        session.OwnerRemovedDishes ??= [];
        var alreadyRemoved = session.OwnerRemovedDishes.FirstOrDefault(d => d.DishId == dishId);
        if (alreadyRemoved is null)
        {
            session.OwnerRemovedDishes.Add(removedItem);
        }
        else
        {
            alreadyRemoved.Quantity += removedItem.Quantity;
        }

        // The guest's cart no longer matches what they submitted the moment the owner changes
        // it, whether or not anything is left in it: showing "submitted" with silently-altered
        // contents would mislead both the guest and the owner. Un-submit on any change.
        if (session.IsSubmitted)
        {
            session.IsSubmitted = false;
        }

        await guestSessionRepository.SaveAsync(session);

        return await BuildSessionsSnapshotAsync(groupReservationId);
    }

    private async Task<GetAllSessionsResponse> BuildSessionsSnapshotAsync(string groupReservationId)
    {
        var sessions = (await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId))
            // the owner has her own path through the Orders table, so an abandoned owner guest
            // session (left over from before this fix) must not show up as a duplicate Owner
            .Where(session => !session.IsOwnerSession)
            .ToList();

        // The owner picks dishes through the regular pre-order flow (Orders table), so the group
        // view must read her order from there and expose it as the Owner participant. Without this
        // the owner never sees her own dishes in "My Pre-Order".
        GroupReservation? group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        Reservation? reservation = group is null ? null : await reservationRepository.GetReservationByIdAsync(group.ReservationId);
        Order? ownerOrder = reservation is null ? null : await orderRepository.GetOrderByReservationIdAsync(reservation.Id);
        bool hasOwnerOrder = ownerOrder is not null &&
            ownerOrder.Dishes.Any(dish => dish.State != OrderItemState.Canceled) &&
            ownerOrder.Status != OrderStatus.Cancelled;

        var dishIds = sessions
            .SelectMany(s => s.Dishes)
            .Select(s => s.DishId)
            .Concat(hasOwnerOrder
                ? ownerOrder!.Dishes.Where(dish => dish.State != OrderItemState.Canceled).Select(dish => dish.DishId)
                : Enumerable.Empty<string>())
            .Distinct()
            .ToArray();

        var allDishes = await dishRepository.GetDishesByIdsAsync(dishIds);
        var foundDishIds = allDishes.Select(dish => dish.Id).ToHashSet(StringComparer.Ordinal);
        var missingDishId = dishIds.FirstOrDefault(dishId => !foundDishIds.Contains(dishId));
        if (missingDishId is not null)
            throw new DishNotFoundException($"Dish '{missingDishId}' in this group's order no longer exists.");

        var dishesById = allDishes.ToDictionary(d => d.Id);

        var participants = new List<Participant>();

        if (hasOwnerOrder)
        {
            var ownerItems = ownerOrder!.Dishes
                .Where(dish => dish.State != OrderItemState.Canceled)
                .Select(d => new OrderItemResponse
            {
                Dish = WithOrderPrice(DishMapper.ToDishResponse(dishesById[d.DishId]), d.UnitPriceAtOrder),
                Quantity = d.Quantity
            }).ToArray();

            decimal ownerTotal = ownerItems.Sum(i => i.Quantity * i.Dish.Price);
            bool ownerSubmitted = ownerOrder.Status == OrderStatus.Submitted || ownerOrder.Status == OrderStatus.SubmittedByCustomer;
            string ownerName = (await userService.GetUserName(ownerOrder.CustomerId)) ?? "Owner";
            participants.Add(new Participant(ownerOrder.OrderId, ParticipantType.Owner, ownerName, ownerItems, ownerTotal, ownerSubmitted));
        }
        else if (reservation is not null && !string.IsNullOrWhiteSpace(reservation.CustomerId))
        {
            // Owner has not picked anything yet; still surface her as a participant so the UI shows "You have not picked any dishes yet".
            string ownerName = (await userService.GetUserName(reservation.CustomerId)) ?? "Owner";
            participants.Add(new Participant(null, ParticipantType.Owner, ownerName, Array.Empty<OrderItemResponse>(), 0m, false));
        }

        foreach (var guestSession in sessions)
        {
            var items = guestSession.Dishes
                .Where(dish => dish.State != OrderItemState.Canceled)
                .Select(d => new OrderItemResponse
            {
                Dish = WithOrderPrice(DishMapper.ToDishResponse(dishesById[d.DishId]), d.UnitPriceAtOrder),
                Quantity = d.Quantity
            }).ToArray();

            decimal total = items.Sum(i => i.Quantity * i.Dish.Price);
            var participant = new Participant(guestSession.Id, ParticipantType.Guest, guestSession.GuestName, items, total, guestSession.IsSubmitted);
            participants.Add(participant);
        }

        return new GetAllSessionsResponse(participants, participants.Sum(p => p.Total));
    }

    private static APIHandler.Models.Dishes.DishResponse WithOrderPrice(
        APIHandler.Models.Dishes.DishResponse dish,
        decimal? unitPriceAtOrder)
    {
        if (unitPriceAtOrder.HasValue)
            dish.Price = unitPriceAtOrder.Value;

        return dish;
    }

    public async Task SubmitCartAsync(string guestSessionId)
    {
        GuestSession guestSession = await GetGuestSessionAsync(guestSessionId);

        if (!guestSession.Dishes.Any())
            throw new CartIsEmptyException();

        await ValidateDishesAsync(guestSession.Dishes);

        guestSession.IsSubmitted = true;
        await guestSessionRepository.SaveAsync(guestSession);
    }

    private async Task<GroupReservation> GetGroupReservationAndValidateAsync(string groupReservationId)
    {
        GroupReservation? group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        ValidateGroupReservation(group);
        return group;
    }

    private async Task<string> ResolveUniqueGuestNameAsync(string groupReservationId, string requestedName, string? ownerDisplayName = null)
    {
        var existingSessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);
        var existingNames = (existingSessions ?? new List<GuestSession>())
            .Select(s => s.GuestName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(ownerDisplayName))
            existingNames.Add(ownerDisplayName);

        if (!existingNames.Contains(requestedName))
            return requestedName;

        for (int suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{requestedName} ({suffix})";
            if (!existingNames.Contains(candidate))
                return candidate;
        }

        return $"{requestedName} ({Guid.NewGuid().ToString("N")[..4]})";
    }

    private async Task<int> GetCurrentParticipantCountAsync(string groupReservationId)
    {
        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);

        // Owner is always 1 active participant. 
        // If sessions is null/empty, it safely evaluates to 0 joined guests + 1 owner = 1.
        return (sessions?.Count(session => !session.IsOwnerSession) ?? 0) + 1;
    }

    private async Task EnsureGuestCounterInitializedAsync(string groupReservationId)
    {
        var groupRecord = await dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = ResourceNames.GroupReservationsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["groupReservationId"] = new AttributeValue { S = groupReservationId }
            },
            ConsistentRead = true,
            ProjectionExpression = "joinedGuestsCount"
        });

        if (groupRecord.Item is not null && groupRecord.Item.ContainsKey("joinedGuestsCount"))
            return;

        int legacyGuestCount = await guestSessionRepository.CountByGroupReservationId(groupReservationId);
        try
        {
            await dynamoDbClient.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = ResourceNames.GroupReservationsTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["groupReservationId"] = new AttributeValue { S = groupReservationId }
                },
                UpdateExpression = "SET joinedGuestsCount = :count",
                ConditionExpression = "attribute_not_exists(joinedGuestsCount)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":count"] = new AttributeValue { N = legacyGuestCount.ToString() }
                }
            });
        }
        catch (ConditionalCheckFailedException)
        {
            // Another join initialized the counter first; use its value.
        }
    }

    private async Task ValidateGroupReservationId(string groupReservationId) => await GetGroupReservationAndValidateAsync(groupReservationId);

    private static void ValidateGroupReservation([NotNull] GroupReservation? group)
    {
        if (group is null)
            throw new GroupReservationNotFoundException();

        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();
    }

    private async Task<GuestSession> GetGuestSessionAsync(string sessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(sessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session {sessionId} not found.");

        if (session.IsSubmitted)
            throw new GuestSessionSubmittedException();

        GroupReservation group = await GetGroupReservationAndValidateAsync(session.GroupReservationId);

        // The owner's own pre-order locks 30 minutes before the reservation starts
        // (OrderService.EnsurePreOrderEditable); guests were never held to that same deadline,
        // so a guest could still add, remove or submit dishes after the kitchen should already
        // be preparing (or after the reservation itself has started).
        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId);
        if (reservation is not null)
        {
            var cutoff = reservation.StartTime.ToUniversalTime().AddMinutes(-30);
            if (DateTime.UtcNow > cutoff)
                throw new ValidationException($"Pre-orders can only be modified up to 30 minutes before the reservation start time ({reservation.StartTime:HH:mm}).");
        }

        return session;
    }

    private async Task ValidateDishesAsync(IEnumerable<OrderItem> orders)
    {
        var items = orders.ToArray();
        if (items.Any(item => item.Quantity < 1 || item.Quantity > OrderItem.MaxQuantityPerDish) ||
            items.GroupBy(item => item.DishId).Any(group => group.Sum(item => (long)item.Quantity) > OrderItem.MaxQuantityPerDish))
        {
            throw new ValidationException($"Each dish quantity must be between 1 and {OrderItem.MaxQuantityPerDish}.");
        }

        IEnumerable<string> dishIds = items.Select(o => o.DishId).ToArray();
        IEnumerable<Dish> dishes = await dishRepository.GetDishesByIdsAsync(dishIds);
        var availableDishes = dishes.ToList();

        var foundDishIds = availableDishes.Select(dish => dish.Id).ToHashSet(StringComparer.Ordinal);
        var missingDishId = dishIds.FirstOrDefault(dishId => !foundDishIds.Contains(dishId));
        if (missingDishId is not null)
            throw new DishNotFoundException($"Dish '{missingDishId}' no longer exists.");

        if (availableDishes.FirstOrDefault(d => d.State != DishState.Available) is Dish dish)
            throw new DishIsNotAvailableException($"The dish '{dish.Name}' ({dish.Id}) is not available.");
    }

    private async Task<string> GetLocationIdForGroupAsync(string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        var table = await tableRepository.GetTableAsync(reservation.TableId)
            ?? throw new ValidationException("Associated table not found.");

        return table.LocationId;
    }

    
    private async Task ValidateReservationOwnerAsync(string groupReservationId, string userId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        if (reservation.CustomerId != userId)
        {
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
        }
    }

    public async Task CreateGroupOwnerSessionAsync(string userId, string groupReservationId)
    {
        var session = new GuestSession()
        {
            Id = Guid.NewGuid().ToString(),
            GroupReservationId = groupReservationId,
            GuestName = "Owner",
            Dishes = [],
            IsOwnerSession = true,
            OwnerUserId = userId,
        };

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task<GetGuestSessionResponse> GetGuestSessionInfoAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException("Guest session was not found.");

        var dishIds = session.Dishes
            .Select(s => s.DishId)
            .Distinct()
            .ToArray();

        var allDishes = await dishRepository.GetDishesByIdsAsync(dishIds);
        var foundDishIds = allDishes.Select(dish => dish.Id).ToHashSet(StringComparer.Ordinal);
        var missingDishId = dishIds.FirstOrDefault(dishId => !foundDishIds.Contains(dishId));
        if (missingDishId is not null)
            throw new DishNotFoundException($"Dish '{missingDishId}' in this guest session no longer exists.");

        var dishesById = allDishes.ToDictionary(d => d.Id);

        var items = session.Dishes
            .Where(dish => dish.State != OrderItemState.Canceled)
            .Select(d => new OrderItemResponse
        {
            Dish = WithOrderPrice(DishMapper.ToDishResponse(dishesById[d.DishId]), d.UnitPriceAtOrder),
            Quantity = d.Quantity
        }).ToArray();

        decimal total = items.Sum(i => i.Quantity * i.Dish.Price);

        string name = session.GuestName;
        if (session.IsOwnerSession)
        {
            var groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(session.GroupReservationId);
            var reservation = await reservationRepository.GetReservationByIdAsync(groupReservation!.ReservationId);
            var user = await userRepository.GetUserByIdAsync(reservation!.CustomerId!);
            name = user!.Attributes.First(a => a.Name == "given_name").Value;
        }

        return new GetGuestSessionResponse(name, items, total, session.IsSubmitted);
    }

}




