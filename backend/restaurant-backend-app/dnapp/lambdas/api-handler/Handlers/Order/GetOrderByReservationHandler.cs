using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Mappers;
using APIHandler.Models.Users;
using APIHandler.Services.Dishes;
using APIHandler.Services.Orders;
using APIHandler.Services.Reservations;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Order;

public class GetOrderByReservationHandler : IApiRequestHandler
{
    private readonly IDishService _dishService;
    private readonly IOrderService _orderService;
    private readonly IReservationService _reservationService;

    public GetOrderByReservationHandler(IDishService dishService, IOrderService orderService, IReservationService reservationService)
    {
        _dishService = dishService ?? throw new ArgumentNullException(nameof(dishService));
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        var reservationId = request.PathParameters?["reservationId"];
        if (string.IsNullOrEmpty(reservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Reservation ID is required.", context.Logger);
        }

        var order = await _orderService.GetOrderByReservationIdAsync(reservationId);
        if (order == null)
        {
            return ResponseBuilder.CreateErrorResponse(404, "Order not found.", context.Logger);
        }

        if (request.RequestContext.Authorizer?.Claims is not { } claims)
        {
            return ResponseBuilder.CreateErrorResponse(403, "Forbidden: Missing claims.", context.Logger);
        }

        var isWaiter = claims.TryGetValue("cognito:groups", out var groups) && groups.Contains(Roles.Waiter.ToString());
        var isOrderCustomer = claims.TryGetValue("sub", out var sub) && sub == order.CustomerId;

        // A waiter may only view orders for reservations assigned to them, not any order in the system.
        var isAssignedWaiter = false;
        if (isWaiter && sub is not null)
        {
            var reservation = await _reservationService.GetReservationByIdAsync(reservationId);
            isAssignedWaiter = reservation is not null && reservation.WaiterId == sub;
        }

        if (!isAssignedWaiter && !isOrderCustomer)
        {
            return ResponseBuilder.CreateErrorResponse(403, "Forbidden: Only the assigned waiter or the customer can access this order.", context.Logger);
        }

        var dishIds = order.Dishes.Select(d => d.DishId).ToList();
        var dishes = await _dishService.GetDishesByIdsAsync(dishIds);

        var orderResponse = OrderMapper.ToOrderResponse(order, dishes);
        return ResponseBuilder.CreateSuccessResponse(orderResponse);
    }
}