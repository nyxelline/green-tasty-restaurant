using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Helpers;
using APIHandler.Models.Reservations;
using APIHandler.Models.Users;
using APIHandler.Services.Feedbacks;
using APIHandler.Services.Orders;
using APIHandler.Services.Reports.Interfaces;
using APIHandler.Services.Reservations;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Reservations;

public class UpdateReservationStatusHandler : IApiRequestHandler
{
    private readonly IReservationService _reservationService;
    private readonly IOrderService _orderService;
    private readonly IFeedbackService _feedbackService;
    private readonly IReportService _reportService;

    public UpdateReservationStatusHandler(
        IReservationService reservationService,
        IOrderService orderService,
        IFeedbackService feedbackService,
        IReportService reportService)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!ValidateRequest(request, context.Logger, out var reservationId, out var updateRequest, out var response))
        {
            return response!;
        }

        var reservation = await _reservationService.GetReservationByIdAsync(reservationId!);
        if (!ValidateReservation(reservation, updateRequest!.NewStatus, context.Logger, out response))
        {
            return response!;
        }

        var callerId = UserContextHelper.GetAuthenticatedUserId(request);
        if (callerId is null || reservation!.WaiterId != callerId)
        {
            return ResponseBuilder.CreateErrorResponse(
                403,
                "You can only update the status of reservations assigned to you.",
                context.Logger);
        }

        if (updateRequest.NewStatus is ReservationStatus.Reserved)
        {
            return ResponseBuilder.CreateErrorResponse(
                400,
                "Cannot update reservation status to Reserved.",
                context.Logger);
        }

        if (!IsAllowedTransition(reservation!.Status, updateRequest.NewStatus))
        {
            return ResponseBuilder.CreateErrorResponse(
                409,
                $"Transition from '{reservation.Status}' to '{updateRequest.NewStatus}' is not allowed.",
                context.Logger);
        }

        bool isCompleted = updateRequest.NewStatus == ReservationStatus.Completed;
        if (isCompleted && !await HasFeedbackAsync(reservationId!) && !await HasOrderAsync(reservationId!))
        {
            return ResponseBuilder.CreateErrorResponse(
                400,
                "Cannot complete reservation without feedback or an order.",
                context.Logger);
        }

        reservation!.Status = updateRequest.NewStatus;
        var updateResult = await _reservationService.UpdateReservationAsync(reservation);
        if (!updateResult)
        {
            return ResponseBuilder.CreateErrorResponse(
                500,
                "Failed to update reservation status. Please try again later.",
                context.Logger);
        }

        if (!isCompleted)
        {
            return ResponseBuilder.CreateSuccessResponse("Reservation status updated successfully.");
        }

        var report = await _reportService.GenerateReportAsync(reservationId!);
        await _reportService.SendReportToSqsAsync(report);


        return ResponseBuilder.CreateSuccessResponse("Reservation status updated successfully.");
    }

    private bool ValidateRequest(
        APIGatewayProxyRequest request,
        ILambdaLogger logger,
        out string? reservationId,
        out UpdateReservationStatusRequest? updateRequest,
        out APIGatewayProxyResponse? response)
    {
        if (!TryGetReservationIdFromPath(request, out reservationId))
        {
            updateRequest = null;
            response = ResponseBuilder.CreateErrorResponse(
                400,
                "Invalid request. Reservation ID is required.",
                logger);
            return false;
        }

        if (!TryParseRequestBody(request, out updateRequest))
        {
            response = ResponseBuilder.CreateErrorResponse(
                400,
                "Invalid request body. Please provide a valid JSON with the new status.",
                logger);
            return false;
        }

        if (!Enum.IsDefined(typeof(ReservationStatus), updateRequest!.NewStatus))
        {
            response = ResponseBuilder.CreateErrorResponse(400, "Unknown reservation status.", logger);
            return false;
        }

        if (!IsWaiter(request))
        {
            response = ResponseBuilder.CreateErrorResponse(
                403,
                "You don't have enough permissions to perform this action.",
                logger);
            return false;
        }

        response = null;
        return true;
    }

    private static bool IsAllowedTransition(ReservationStatus currentStatus, ReservationStatus newStatus)
        => (currentStatus, newStatus) is
            (ReservationStatus.Reserved, ReservationStatus.InProgress) or
            (ReservationStatus.InProgress, ReservationStatus.Completed);

    private bool IsWaiter(APIGatewayProxyRequest request)
    {
        if (request.RequestContext.Authorizer is null)
        {
            return false;
        }

        if (request.RequestContext.Authorizer.Claims is null)
        {
            return false;
        }

        if (!request.RequestContext.Authorizer.Claims.TryGetValue("cognito:groups", out var groups))
        {
            return false;
        }

        return groups.Contains(Roles.Waiter.ToString());
    }

    private bool TryGetReservationIdFromPath(APIGatewayProxyRequest request, out string? reservationId)
    {
        if (request.PathParameters is null || !request.PathParameters.TryGetValue("reservationId", out reservationId))
        {
            reservationId = null;
            return false;
        }

        return !string.IsNullOrEmpty(reservationId);
    }

    private bool TryParseRequestBody(APIGatewayProxyRequest request, out UpdateReservationStatusRequest? updateRequest)
    {
        try
        {
            updateRequest = System.Text.Json.JsonSerializer.Deserialize<UpdateReservationStatusRequest>(request.Body);
            return updateRequest != null;
        }
        catch
        {
            updateRequest = null;
            return false;
        }
    }

    private bool ValidateReservation(
        Reservation? reservation,
        ReservationStatus newStatus,
        ILambdaLogger logger,
        out APIGatewayProxyResponse? response)
    {
        if (reservation is null)
        {
            response = ResponseBuilder.CreateErrorResponse(
                404,
                "Reservation not found.",
                logger);
            return false;
        }

        // Both checks below used to build an error response and then fall through to
        // "response = null; return true", so neither ever actually blocked the request.
        if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Completed)
        {
            response = ResponseBuilder.CreateErrorResponse(
                400,
                "Cannot update a cancelled or completed reservation status.",
                logger);
            return false;
        }

        if (newStatus == reservation.Status)
        {
            response = ResponseBuilder.CreateErrorResponse(
                400,
                "The new status is the same as the current status.",
                logger);
            return false;
        }

        response = null;
        return true;
    }

    private async Task<bool> HasFeedbackAsync(string reservationId)
    {
        var feedback = await _feedbackService.GetFeedbackForReservationAsync(reservationId);
        return feedback?.Count > 0;
    }

    private async Task<bool> HasOrderAsync(string reservationId)
    {
        var order = await _orderService.GetOrderByReservationIdAsync(reservationId);
        return order is not null && order.Dishes.Any(dish => dish.State != APIHandler.Constants.Orders.OrderItemState.Canceled);
    }
}