using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using AutoFixture.Xunit2;
using Moq;
using NPOI.POIFS.Storage;
using System.Security.Cryptography;
using System.Text;

namespace APIHandler.Tests.Services;

public class GuestSessionServiceTests
{
    private readonly Mock<IGuestSessionRepository> _mockGuestSessionRepository = new();
    private readonly Mock<IGroupReservationRepository> _mockGroupReservationRepository = new();
    private readonly Mock<IDishRepository> _mockDishRepository = new();
    private readonly Mock<IReservationRepository> _mockReservationRepository = new();
    private readonly Mock<IUserRepository> _mockUserRepository = new();
    private readonly Mock<ITableRepository> _mockTableRepository = new();
    private readonly Mock<APIHandler.Repositories.Orders.IOrderRepository> _mockOrderRepository = new();
    private readonly Mock<APIHandler.Services.Auth.IUserService> _mockUserService = new();
    private readonly Mock<IAmazonDynamoDB> _mockDynamoDbClient = new();
    private readonly GuestSessionService _guestSessionService;

    public GuestSessionServiceTests()
    {
        Environment.SetEnvironmentVariable("groupReservationsTable", "test-group-reservations");
        Environment.SetEnvironmentVariable("guestSessionTable", "test-guest-sessions");

        _guestSessionService = new GuestSessionService(
            _mockGroupReservationRepository.Object,
            _mockGuestSessionRepository.Object,
            _mockDishRepository.Object,
            _mockReservationRepository.Object,
            _mockUserRepository.Object,
            _mockTableRepository.Object,
            _mockOrderRepository.Object,
            _mockUserService.Object,
            _mockDynamoDbClient.Object);

        _mockDynamoDbClient
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new TransactWriteItemsResponse());
        _mockDynamoDbClient
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });
    }
    [Theory]
    [InlineAutoData(0)]
    [InlineAutoData(3)]
    public async Task CreateGuestSessionAsync_CorrectData_ReturnsSessionToken(
        int guestCount,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;

        parentReservation.GuestCount = 5;
        parentReservation.Status = ReservationStatus.Reserved;
        parentReservation.StartTime = DateTime.UtcNow.AddHours(1);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([]);

        TransactWriteItemsRequest? transaction = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()));
        _mockDynamoDbClient
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback<TransactWriteItemsRequest, System.Threading.CancellationToken>((request, _) => transaction = request)
            .ReturnsAsync(new TransactWriteItemsResponse());

        _mockGuestSessionRepository
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(guestCount);

        var response = await _guestSessionService.CreateGuestSessionAsync(request);

        Assert.NotEmpty(response.SessionId);
        Assert.NotEmpty(response.SessionToken);
        Assert.NotNull(transaction);
        var guestItem = transaction.TransactItems[1].Put.Item;
        Assert.Equal(response.SessionId, guestItem["guestSessionId"].S);
        Assert.Equal(groupReservation.GroupReservationId, guestItem["groupReservationId"].S);
        Assert.Equal(request.GuestName, guestItem["guestName"].S);
        Assert.NotEmpty(guestItem["guestSessionTokenHash"].S);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_InvalidGroupReservationId_ThrowsGroupReservationNotFoundException(CreateGuestSessionRequest request)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(request.GroupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_GroupReservationExpired_ThrowsGroupReservationExpiredException(CreateGuestSessionRequest request, GroupReservation groupReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(request.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldAddNewDish_WhenDishDoesNotExistInSession(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        parentReservation.StartTime = DateTime.UtcNow.AddHours(2);
        dish.State = DishState.Available;
        int quantityToAdd = 2;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        await _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantityToAdd);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(dish.Id, guestSession.Dishes[0].DishId);
        Assert.Equal(quantityToAdd, guestSession.Dishes[0].Quantity);
        Assert.Equal(OrderItemState.Pending, guestSession.Dishes[0].State);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldIncreaseQuantity_WhenDishAlreadyExists(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        int initialQuantity = 3;
        int quantityToAdd = 2;
        guestSession.Dishes = [new OrderItem { DishId = dish.Id, Quantity = initialQuantity, State = OrderItemState.Pending }];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        parentReservation.StartTime = DateTime.UtcNow.AddHours(2);
        dish.State = DishState.Available;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        await _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantityToAdd);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(initialQuantity + quantityToAdd, guestSession.Dishes[0].Quantity);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_QuantityWouldExceedLimit_ThrowsWithoutSaving(
        GuestSession session, GroupReservation group, Reservation reservation, Table table, Dish dish)
    {
        session.GroupReservationId = group.GroupReservationId;
        session.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 98, State = OrderItemState.Pending }];
        group.Status = GroupReservationStatus.Open;
        group.ReservationId = reservation.Id;
        reservation.TableId = table.Id;
        reservation.StartTime = DateTime.UtcNow.AddHours(2);
        dish.State = DishState.Available;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(session.Id)).ReturnsAsync(session);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(group.GroupReservationId)).ReturnsAsync(group);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        await Assert.ThrowsAsync<ValidationException>(() => _guestSessionService.AddDishAsync(session.Id, dish.Id, 2));

        Assert.Equal(98, session.Dishes[0].Quantity);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(0)]
    [InlineAutoData(-1)]
    public async Task AddDishAsync_ShouldThrowValidationException_WhenQuantityIsZeroOrLess(int quantity, string guestSessionId, Dish dish)
    {
        dish.State = DishState.Available;

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.AddDishAsync(guestSessionId, dish.Id, quantity));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowGuestSessionNotFoundException_WhenSessionNotFound(string guestSessionId, Dish dish)
    {
        dish.State = DishState.Available;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        var exception = await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.AddDishAsync(guestSessionId, dish.Id, 2));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowDishNotFound_WhenDishNotFound(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, string dishId)
    {
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        parentReservation.StartTime = DateTime.UtcNow.AddHours(2);

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);

        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.AddDishAsync(guestSession.Id, dishId, quantity: 2));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowDishNotAvailable_WhenDishIsNotAvailable(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        dish.State = DishState.OutOfStock;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        parentReservation.StartTime = DateTime.UtcNow.AddHours(2);

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        var exception = await Assert.ThrowsAsync<DishIsNotAvailableException>(() =>
            _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantity: 2));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task AddDishAsync_ShouldThrowGroupReservationExpired_WhenGroupStatusIsNotOpen(GroupReservationStatus status, Type exceptionType, GuestSession guestSession, GroupReservation groupReservation, Dish dish)
    {
        dish.State = DishState.Available;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = status;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        var exception = await Assert.ThrowsAsync(exceptionType, () =>
            _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantity: 2));
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldDecreaseQuantity_WhenQuantityIsGreaterThanAmountToRemove(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        int initialQuantity = 5;
        int quantityToRemove = 2;
        guestSession.Dishes =
        [
            new OrderItem { DishId = dishId, Quantity = initialQuantity, State = OrderItemState.Pending }
        ];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);


        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, quantityToRemove);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(initialQuantity - quantityToRemove, guestSession.Dishes[0].Quantity);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldRemoveDishFromList_WhenQuantityToRemoveIsEqualOrGreater(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        int initialQuantity = 2;
        int quantityToRemove = 3;
        guestSession.Dishes =
        [
            new OrderItem { DishId = dishId, Quantity = initialQuantity, State = OrderItemState.Pending }
        ];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, quantityToRemove);

        Assert.Empty(guestSession.Dishes);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowDishNotFoundException_WhenDishDoesNotExistInSession(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
           .ReturnsAsync(groupReservation);

        var exception = await Assert.ThrowsAsync<DishNotFoundException>(() =>
            _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, 2));

        Assert.Contains("not found in guest session", exception.Message);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowValidationException_WhenQuantityIsZeroOrLess(string guestSessionId, string dishId)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, 0));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, -5));
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowGuestSessionNotFoundException_WhenSessionNotFound(string guestSessionId, string dishId)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        var exception = await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, 2));
    }
    [Theory]
    [InlineAutoData(1, 0)]
    [InlineAutoData(1, 1)]
    [InlineAutoData(2, 1)]
    [InlineAutoData(2, 2)]
    public async Task CreateGuestSessionAsync_WhenCapacityReached_ThrowsGuestLimitExceededException(
        int reservationCapacity,
        int guestCount,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.ReservationId = parentReservation.Id;

        parentReservation.GuestCount = reservationCapacity;
        parentReservation.Status = ReservationStatus.Reserved;
        parentReservation.StartTime = DateTime.UtcNow.AddHours(1);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);

        _mockGuestSessionRepository
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(guestCount);

        _mockDynamoDbClient
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("Guest limit reached"));
        _mockDynamoDbClient
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["joinedGuestsCount"] = new() { N = Math.Max(0, reservationCapacity - 1).ToString() }
                }
            });

        var exception = await Assert.ThrowsAsync<GuestLimitExceededException>(() => _guestSessionService.CreateGuestSessionAsync(request));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_MissingParentReservation_ThrowsGroupReservationExpiredException(
        CreateGuestSessionRequest request,
        GroupReservation groupReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync((Reservation?)null);

        var exception = await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_ValidTokenAndId_ReturnsTrue(GuestSession guestSession, byte[] tokenBytes)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        guestSession.SessionTokenHash = hashString;

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync(guestSession);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSession.Id);

        Assert.True(result);
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_InvalidToken_ReturnsFalse(byte[] tokenBytes, string guestSessionId)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync((GuestSession?)null);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSessionId);

        Assert.False(result);
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_InvalidGuestSessionId_ReturnsFalse(GuestSession guestSession, byte[] tokenBytes, string guestSessionId)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        guestSession.SessionTokenHash = hashString;

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync(guestSession);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSessionId);

        Assert.False(result);
    }
    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_Valid_SubmitsSuccessfuilly(GuestSession guestSession, GroupReservation groupReservation, Dish[] dishes)
    {
        foreach (var dish in dishes)
            dish.State = DishState.Available;

        guestSession.Dishes = dishes.Select(dish => new OrderItem
        {
            DishId = dish.Id,
            Quantity = 1,
            State = OrderItemState.Pending
        }).ToList();
        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
           .ReturnsAsync(groupReservation);

        _mockDishRepository
            .Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(dishes);

        GuestSession? session = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => session = s);

        await _guestSessionService.SubmitCartAsync(guestSession.Id);

        Assert.NotNull(session);
        Assert.True(session.IsSubmitted);
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_DishNoLongerExists_ThrowsAndDoesNotSave(
        GuestSession session, GroupReservation group, Reservation reservation)
    {
        session.GroupReservationId = group.GroupReservationId;
        session.IsSubmitted = false;
        session.Dishes = [new OrderItem { DishId = "missing-dish", Quantity = 1 }];
        group.Status = GroupReservationStatus.Open;
        group.ReservationId = reservation.Id;
        reservation.Status = ReservationStatus.Reserved;
        reservation.StartTime = DateTime.UtcNow.AddHours(1);

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(session.Id)).ReturnsAsync(session);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(group.GroupReservationId)).ReturnsAsync(group);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockDishRepository.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync([]);

        await Assert.ThrowsAsync<DishNotFoundException>(() => _guestSessionService.SubmitCartAsync(session.Id));
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_SessionNotFound_ThrowsGuestSessionNotFound(string guestSessionId)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _guestSessionService.SubmitCartAsync(guestSessionId));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task SubmitCartAsync_GroupReservationNotOpen_ThrowsException(GroupReservationStatus status, Type exceptionType, GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = status;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(exceptionType, () => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_GuestSessionHasNoDishes_ThrowsCartIsEmptyException(GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<CartIsEmptyException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_GuestSessionSubmitted_ThrowsAlreadySubmited(GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.IsSubmitted = true;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GuestSessionSubmittedException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [InlineAutoData(DishState.OutOfStock)]
    [InlineAutoData(DishState.OnStop)]
    public async Task SubmitCartAsync_DishNotAvailable_ThrowsDishNotAvailable(DishState state, Dish dish, GuestSession guestSession, GroupReservation groupReservation)
    {
        dish.State = state;
        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        guestSession.Dishes = 
        [
            new OrderItem()
            {
                DishId = dish.Id,
                Quantity = 1,
            }
        ];

        _mockDishRepository
            .Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([dish]);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
           .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<DishIsNotAvailableException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }
    [Theory]
    [MemberAutoData(nameof(PepareDishesForGuestSession))]
    public async Task GetGuestSessionInfoAsync_Guest_ReturnsAsync(
        IEnumerable<string> dishIds,
        IEnumerable<Dish> dishes,
        List<OrderItem> orderItems,
        decimal expectedTotal,
        GuestSession session)
    {
        session.Dishes = orderItems;
        session.IsOwnerSession = false;

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(dishIds))
            .ReturnsAsync(dishes);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        GetGuestSessionResponse result = await _guestSessionService.GetGuestSessionInfoAsync(session.Id);

        Assert.Equal(session.GuestName, result.Name);
        Assert.Equal(dishes.Count(), result.Items.Count());
        Assert.Equal(expectedTotal, result.Total);
    }

    [Theory]
    [MemberAutoData(nameof(PepareDishesForGuestSession))]
    public async Task GetGuestSessionInfoAsync_GroupOwner_ReturnsAsync(
        IEnumerable<string> dishIds,
        IEnumerable<Dish> dishes,
        List<OrderItem> orderItems,
        decimal expectedTotal,
        GuestSession session,
        GroupReservation groupReservation,
        Reservation reservation,
        string customerId,
        string ownerName,
        UserType userType)
    {
        session.Dishes = orderItems;
        session.IsOwnerSession = true;
        session.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = customerId;
        userType.Attributes.Add(new AttributeType()
        {
            Name = "given_name",
            Value = ownerName,
        });

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(dishIds))
            .ReturnsAsync(dishes);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(customerId))
            .ReturnsAsync(userType);

        GetGuestSessionResponse result = await _guestSessionService.GetGuestSessionInfoAsync(session.Id);

        Assert.Equal(ownerName, result.Name);
        Assert.Equal(dishes.Count(), result.Items.Count());
        Assert.Equal(expectedTotal, result.Total);
    }

    public static TheoryData<IEnumerable<string>, IEnumerable<Dish>, List<OrderItem>, decimal> PepareDishesForGuestSession()
    {
        var dishMap = new Dictionary<string, Dish>()
        {
            ["1"] = new()
            {
                Id = "1",
                Price = 1
            },
            ["2"] = new()
            {
                Id = "2",
                Price = 2
            },
            ["3"] = new()
            {
                Id = "3",
                Price = 3
            }
        };

        var orderItems = new List<OrderItem>
        {
            new OrderItem()
                    {
                        DishId = "1",
                        Quantity = 2
                    },
                    new OrderItem()
                    {
                        DishId = "2",
                        Quantity = 3
                    },
                    new OrderItem()
                    {
                        DishId = "3",
                        Quantity = 1
                    }
        };

        var expectedTotal = orderItems.Sum(o => dishMap[o.DishId].Price * o.Quantity);

        return new()
        {
            { dishMap.Keys, dishMap.Values, orderItems, expectedTotal }
        };
    }

    private void SetupOwnerContext(string userId, GroupReservation group, Reservation reservation, GuestSession session)
    {
        group.Status = GroupReservationStatus.Open;
        group.ReservationId = reservation.Id;
        reservation.CustomerId = userId;
        session.GroupReservationId = group.GroupReservationId;

        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(group.GroupReservationId)).ReturnsAsync(group);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(session.Id)).ReturnsAsync(session);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsOwnerAsync_OwnerRemovesDish_RemovesLineAndRemembersIt(
        string userId, GroupReservation group, Reservation reservation, GuestSession session, Dish removedDish, Dish keptDish)
    {
        session.Dishes =
        [
            new OrderItem { DishId = removedDish.Id, Quantity = 2, State = OrderItemState.Pending },
            new OrderItem { DishId = keptDish.Id, Quantity = 1, State = OrderItemState.Pending }
        ];
        session.OwnerRemovedDishes = null;
        session.IsSubmitted = true;
        SetupOwnerContext(userId, group, reservation, session);

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([session]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([keptDish]);

        var response = await _guestSessionService.RemoveDishAsOwnerAsync(userId, group.GroupReservationId, session.Id, removedDish.Id);

        Assert.Single(session.Dishes);
        Assert.Equal(keptDish.Id, session.Dishes[0].DishId);
        Assert.NotNull(session.OwnerRemovedDishes);
        var removed = Assert.Single(session.OwnerRemovedDishes);
        Assert.Equal(removedDish.Id, removed.DishId);
        Assert.Equal(2, removed.Quantity);
        Assert.False(session.IsSubmitted);
        Assert.Equal(2, response.Participants.Count());
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(session), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsOwnerAsync_LastDishRemoved_MarksSessionAsNotSubmitted(
        string userId, GroupReservation group, Reservation reservation, GuestSession session, Dish dish)
    {
        session.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 1, State = OrderItemState.Pending }];
        session.IsSubmitted = true;
        SetupOwnerContext(userId, group, reservation, session);

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([session]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);

        await _guestSessionService.RemoveDishAsOwnerAsync(userId, group.GroupReservationId, session.Id, dish.Id);

        Assert.Empty(session.Dishes);
        Assert.False(session.IsSubmitted);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsOwnerAsync_UserIsNotOwner_ThrowsActionForbiddenException(
        string userId, string otherUserId, GroupReservation group, Reservation reservation, GuestSession session, Dish dish)
    {
        session.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 1, State = OrderItemState.Pending }];
        SetupOwnerContext(otherUserId, group, reservation, session);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.RemoveDishAsOwnerAsync(userId, group.GroupReservationId, session.Id, dish.Id));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsOwnerAsync_DishIsNotInSession_ThrowsDishNotFoundException(
        string userId, string missingDishId, GroupReservation group, Reservation reservation, GuestSession session, Dish dish)
    {
        session.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 1, State = OrderItemState.Pending }];
        SetupOwnerContext(userId, group, reservation, session);

        await Assert.ThrowsAsync<DishNotFoundException>(() =>
            _guestSessionService.RemoveDishAsOwnerAsync(userId, group.GroupReservationId, session.Id, missingDishId));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsOwnerAsync_SessionBelongsToAnotherGroup_ThrowsValidationException(
        string userId, GroupReservation group, Reservation reservation, GuestSession session, Dish dish, string anotherGroupId)
    {
        session.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 1, State = OrderItemState.Pending }];
        SetupOwnerContext(userId, group, reservation, session);
        session.GroupReservationId = anotherGroupId;

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsOwnerAsync(userId, group.GroupReservationId, session.Id, dish.Id));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsOwnerAsync_GroupAlreadySubmitted_ThrowsGroupReservationSubmittedException(
        string userId, GroupReservation group, Reservation reservation, GuestSession session, Dish dish)
    {
        session.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 1, State = OrderItemState.Pending }];
        SetupOwnerContext(userId, group, reservation, session);
        group.Status = GroupReservationStatus.Submitted;

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() =>
            _guestSessionService.RemoveDishAsOwnerAsync(userId, group.GroupReservationId, session.Id, dish.Id));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_ClosedGroup_ThrowsAndDoesNotDelete(
        string userId, GroupReservation group, Reservation reservation, GuestSession session)
    {
        SetupOwnerContext(userId, group, reservation, session);
        session.GroupReservationId = group.GroupReservationId;
        group.Status = GroupReservationStatus.Closed;

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, group.GroupReservationId, session.Id));

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_OwnerRequests_ReturnsSnapshotWithOwnerMarkedAsOwner(
        string userId, GroupReservation group, Reservation reservation, GuestSession ownerSession, GuestSession guestSession, Dish dish)
    {
        ownerSession.IsOwnerSession = true;
        ownerSession.Dishes = [];
        guestSession.IsOwnerSession = false;
        guestSession.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 2, State = OrderItemState.Pending }];
        SetupOwnerContext(userId, group, reservation, guestSession);

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([ownerSession, guestSession]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([dish]);

        var response = await _guestSessionService.GetGroupSessionsAsync(userId, group.GroupReservationId);

        Assert.Equal(2, response.Participants.Count());
        Assert.Contains(response.Participants, p => p.Id is null && p.Type == ParticipantType.Owner);
        Assert.Contains(response.Participants, p => p.Id == guestSession.Id && p.Type == ParticipantType.Guest);
        Assert.Equal(dish.Price * 2, response.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_UserIsNotOwner_ThrowsActionForbiddenException(
        string userId, string otherUserId, GroupReservation group, Reservation reservation, GuestSession session)
    {
        SetupOwnerContext(otherUserId, group, reservation, session);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.GetGroupSessionsAsync(userId, group.GroupReservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupBillAsync_Owner_ReturnsPerGuestSubtotalsAndTotal(
        string userId, GroupReservation group, Reservation reservation, GuestSession session, Dish dish)
    {
        session.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 2, State = OrderItemState.Pending, UnitPriceAtOrder = 10m }];
        dish.Price = 20m;
        session.IsOwnerSession = false;
        SetupOwnerContext(userId, group, reservation, session);

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([session]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([dish]);

        var bill = await _guestSessionService.GetGroupBillAsync(userId, group.GroupReservationId);

        Assert.Equal(2, bill.Guests.Count);
        Assert.Contains(bill.Guests, billGuest => billGuest.IsOwner && billGuest.Subtotal == 0);
        var guest = Assert.Single(bill.Guests.Where(billGuest => billGuest.GuestSessionId == session.Id));
        Assert.Equal(session.Id, guest.GuestSessionId);
        Assert.Equal(20m, guest.Subtotal);
        Assert.Equal(20m, bill.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupBillAsync_UserIsNotOwner_ThrowsActionForbiddenException(
        string userId, string otherUserId, GroupReservation group, Reservation reservation, GuestSession session)
    {
        SetupOwnerContext(otherUserId, group, reservation, session);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.GetGroupBillAsync(userId, group.GroupReservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGuestBillAsync_SubmittedGuest_StillSeesOwnShare(
        GroupReservation group, GuestSession session, GuestSession otherSession, Dish dish, Dish otherDish)
    {
        session.GroupReservationId = group.GroupReservationId;
        otherSession.GroupReservationId = group.GroupReservationId;
        session.IsOwnerSession = false;
        otherSession.IsOwnerSession = false;
        session.IsSubmitted = true;
        session.Dishes = [new OrderItem { DishId = dish.Id, Quantity = 1, State = OrderItemState.Pending }];
        otherSession.Dishes = [new OrderItem { DishId = otherDish.Id, Quantity = 1, State = OrderItemState.Pending }];

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(session.Id)).ReturnsAsync(session);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([session, otherSession]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([dish, otherDish]);

        var bill = await _guestSessionService.GetGuestBillAsync(session.Id);

        Assert.Equal(session.Id, bill.GuestSessionId);
        Assert.Equal(dish.Price, bill.Subtotal);
        Assert.Single(bill.Lines);
    }

    [Theory]
    [AutoData]
    public async Task GetGuestBillAsync_UnknownSession_ThrowsGuestSessionNotFoundException(string guestSessionId)
    {
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSessionId)).ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.GetGuestBillAsync(guestSessionId));
    }
}
