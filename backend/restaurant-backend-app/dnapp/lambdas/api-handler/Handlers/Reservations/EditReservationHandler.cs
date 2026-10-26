using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Helpers;
using APIHandler.Mappers;
using APIHandler.Models.Reservations;
using APIHandler.Models.Users;
using APIHandler.Services.Locations;
using APIHandler.Services.Orders;
using APIHandler.Services.Reservations;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Reservations;

public class EditReservationHandler : IApiRequestHandler
{
    private static readonly string ReservationsTableName = ResourceNames.ReservationsTableName;
    private static readonly string TablesTableName = ResourceNames.TablesTableName;
    private static readonly string ReservationsTableGlobalIndex = ResourceNames.ReservationsTableGlobalIndex;

    private readonly IDynamoDBContext _dbContext;
    private readonly ILocationService _locationService;
    private readonly IReservationService _reservationService;
    private readonly IOrderService _orderService;

    public EditReservationHandler(IDynamoDBContext dbContext, ILocationService locationService, IReservationService reservationService, IOrderService orderService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!TryDeserializeRequestBody(request.Body, out EditReservationRequest? editRequest))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Invalid request body", context.Logger);
        }

        var claims = request.RequestContext.Authorizer?.Claims;
        if (claims is not { Count: > 0 } || !claims.TryGetValue("sub", out var userId))
        {
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized", context.Logger);
        }

        var roles = RoleHelper.ParseRoles(claims.TryGetValue("cognito:groups", out var groupsJson) ? groupsJson : null);
        var activeRole = RoleHelper.DetermineActiveRole(roles);

        var reservation = await _dbContext.LoadAsync<Reservation>(
            editRequest!.ReservationId,
            DynamoConfig.LoadConfig(ReservationsTableName));

        if (reservation == null)
        {
            return ResponseBuilder.CreateErrorResponse(404, "Reservation not found.", context.Logger);
        }

        if (!IsUserAuthorizedToModifyReservation(activeRole, userId, reservation))
        {
            return ResponseBuilder.CreateErrorResponse(403, "Not authorized to edit this reservation.", context.Logger);
        }

        // A cancelled or completed reservation is done: editing its time/guest count here was
        // previously allowed and would silently "resurrect" it without going through the normal
        // booking flow (and without keeping its group reservation in sync).
        if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Completed)
        {
            return ResponseBuilder.CreateErrorResponse(400, "Cannot edit a cancelled or completed reservation.", context.Logger);
        }

        var timeDiffMinutes = (editRequest.StartTime.ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
        if (timeDiffMinutes < 15)
        {
            return ResponseBuilder.CreateErrorResponse(400, "Must edit at least 15 minutes in advance.", context.Logger);
        }

        if (editRequest.StartTime >= editRequest.EndTime)
        {
            return ResponseBuilder.CreateErrorResponse(400, "End time must be after start time.", context.Logger);
        }

        var table = await _dbContext.LoadAsync<Table>(reservation.TableId, DynamoConfig.LoadConfig(TablesTableName));
        if (table == null)
        {
            return ResponseBuilder.CreateErrorResponse(404, "Table not found.", context.Logger);
        }

        if (editRequest.GuestCount < 1 || table.Capacity < editRequest.GuestCount)
        {
            return ResponseBuilder.CreateErrorResponse(400, "Guest count must be between 1 and table capacity.", context.Logger);
        }

        if (await HasOverlapAsync(reservation.Id, reservation.TableId, editRequest.StartTime, editRequest.EndTime))
        {
            return ResponseBuilder.CreateErrorResponse(409, "Time overlaps with existing reservation.", context.Logger);
        }

        reservation.StartTime = editRequest.StartTime;
        reservation.EndTime = editRequest.EndTime;
        reservation.GuestCount = editRequest.GuestCount;

        // Route through the reservation service (not a raw SaveAsync) so a guest-count change
        // keeps the associated GroupReservation in sync (created/closed as needed), the same as
        // every other path that updates a reservation.
        var saved = await _reservationService.UpdateReservationAsync(reservation);
        if (!saved)
        {
            context.Logger.LogError($"[ERROR] Failed to save reservation: {reservation.Id}");
            return ResponseBuilder.CreateErrorResponse(500, "Failed to save reservation", context.Logger);
        }

        var location = await TryGetLocationAsync(table.Id);
        if (location == null)
        {
            return ResponseBuilder.CreateErrorResponse(404, "Location not foun.d", context.Logger);
        }

        // Pass the reservation's order so the response's hasSubmittedPreOrder reflects reality
        // after the edit (it was previously omitted, always reporting no submitted pre-order).
        var order = await _orderService.GetOrderByReservationIdAsync(reservation.Id);
        var response = ReservationMapper.ToReservationResponse(reservation, location.Address, order);
        return ResponseBuilder.CreateSuccessResponse(response);
    }

    private static bool IsUserAuthorizedToModifyReservation(string activeRole, string userId, Reservation reservation)
    {
        if (activeRole == Roles.Waiter.ToString())
            return reservation.WaiterId == userId;

        if (activeRole == Roles.Customer.ToString())
            return reservation.CustomerId == userId;

        return false;
    }

    private static bool TryDeserializeRequestBody(string body, out EditReservationRequest? request)
    {
        try
        {
            request = JsonSerializer.Deserialize<EditReservationRequest>(body);
            return request != null;
        }
        catch (JsonException)
        {
            request = null;
            return false;
        }
    }

    private async Task<bool> HasOverlapAsync(string reservationId, string tableId, DateTime startTime, DateTime endTime)
    {
        var queryConfig = DynamoConfig.QueryConfig(ReservationsTableName);
        queryConfig.IndexName = ReservationsTableGlobalIndex;
        queryConfig.ConsistentRead = false;
        var reservations = await _dbContext.QueryAsync<Reservation>(tableId, queryConfig).GetRemainingAsync();
        return reservations.Any(r =>
            r.Id != reservationId &&
            r.Status is not (ReservationStatus.Cancelled or ReservationStatus.Completed) &&
            IsOverlapOrClose(r.StartTime, r.EndTime, startTime, endTime));
    }

    private static bool IsOverlapOrClose(DateTime startTime1, DateTime endTime1, DateTime startTime2, DateTime endTime2, int differenceMinutes = 15)
    {
        bool overlap = startTime1 < endTime2 && startTime2 < endTime1;
        bool closeEnough = Math.Abs((startTime1 - endTime2).TotalMinutes) < differenceMinutes ||
            Math.Abs((startTime2 - endTime1).TotalMinutes) < differenceMinutes;

        return overlap || closeEnough;
    }

    private async Task<Location?> TryGetLocationAsync(string tableId)
    {
        try
        {
            return await _locationService.GetLocationByTableIdAsync(tableId);
        }
        catch (Exception)
        {
            return null;
        }
    }
}