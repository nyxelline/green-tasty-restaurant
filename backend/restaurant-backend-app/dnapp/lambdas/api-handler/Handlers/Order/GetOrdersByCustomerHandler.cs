using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Entities;
using APIHandler.Helpers;
using APIHandler.Mappers;
using APIHandler.Repositories.Reservations;
using APIHandler.Services.Dishes;
using APIHandler.Services.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Order;

public class GetOrdersByCustomerHandler : IApiRequestHandler
{
    private readonly IDishService _dishService;
    private readonly IOrderService _orderService;
    private readonly IReservationRepository _reservationRepository;

    public GetOrdersByCustomerHandler(
        IDishService dishService,
        IOrderService orderService,
        IReservationRepository reservationRepository)
    {
        _dishService = dishService ?? throw new ArgumentNullException(nameof(dishService));
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (request.RequestContext.Authorizer?.Claims is null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var userId))
        {
            return ResponseBuilder.CreateErrorResponse(401, "You should be signed in to access orders.", context.Logger);
        }

        var orders = await _orderService.GetOrdersByCustomerIdAsync(userId);
        orders = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();

        // The cart only shows pre-orders for reservations that haven't finished yet. A
        // reservation's status isn't a reliable signal here (nothing transitions it to
        // Completed just because its end time passed), so this compares against the
        // reservation's real EndTime instead. An order whose reservation can no longer be
        // found is stale/orphaned data and is hidden too - it can't be "current" if the
        // reservation it belongs to doesn't exist any more.
        var reservations = await _reservationRepository.GetReservationsByCustomerIdAsync(userId);
        var reservationEndTimes = reservations
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.First().EndTime);

        orders = orders
            .Where(o => reservationEndTimes.TryGetValue(o.ReservationId, out var endTime) && endTime >= DateTime.UtcNow)
            .ToList();

        var dishIds = orders.SelectMany(o => o.Dishes.Select(d => d.DishId)).Distinct().ToList();
        var dishes = await _dishService.GetDishesByIdsAsync(dishIds);

        var orderResponses = orders.Select(o => OrderMapper.ToOrderResponse(o, dishes)).ToList();
        return ResponseBuilder.CreateSuccessResponse(orderResponses);
    }
}