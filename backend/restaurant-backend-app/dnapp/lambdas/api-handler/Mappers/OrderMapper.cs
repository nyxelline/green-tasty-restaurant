using APIHandler.Entities;
using APIHandler.Models.Orders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace APIHandler.Mappers;

public static class OrderMapper
{
    public static GetOrderResponse ToOrderResponse(Order order, IEnumerable<Dish> dishes)
    {
        return new GetOrderResponse
        {
            OrderId = order.OrderId,
            ReservationId = order.ReservationId,
            ReservationAddress = order.ReservationAddress,
            ReservationTime = order.ReservationTime,
            Status = order.Status,
            // Canceled dishes stay in the response (not filtered out) so the customer can see
            // which items the waiter rejected, instead of the dish silently vanishing from
            // their order with no explanation.
            OrderItems = order.Dishes
                .Where(d => dishes.Any(dish => dish.Id == d.DishId))
                .Select(d => new OrderItemResponse
                {
                    Dish = ToDishResponse(d, dishes.First(dish => dish.Id == d.DishId)),
                    Quantity = d.Quantity,
                    State = d.State
                }).ToList(),
        };
    }

    public static Order ToOrderEntity(CreateOrderRequest request, string customerId, string reservationAddress, string reservationTime)
    {
        return new Order
        {
            OrderId = Guid.NewGuid().ToString(),
            CustomerId = customerId,
            ReservationId = request.ReservationId,
            ReservationAddress = reservationAddress,
            ReservationTime = reservationTime,
            Dishes = request.OrderItems ?? [],
            Status = OrderStatus.Created
        };
    }

    private static APIHandler.Models.Dishes.DishResponse ToDishResponse(OrderItem item, Dish dish)
    {
        var response = DishMapper.ToDishResponse(dish);
        if (item.UnitPriceAtOrder.HasValue)
            response.Price = item.UnitPriceAtOrder.Value;

        return response;
    }
}