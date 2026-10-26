using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Services.Dishes;
using APIHandler.Services.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.Orders;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IReservationRepository _reservationRepository;
    private readonly IDishService _dishService;
    private readonly INotificationService _notificationService;

    public OrderService(
        IOrderRepository orderRepository,
        IReservationRepository reservationRepository,
        IDishService dishService,
        INotificationService notificationService)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
        _dishService = dishService ?? throw new ArgumentNullException(nameof(dishService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    public async Task<IEnumerable<Order>> GetOrdersByCustomerIdAsync(string customerId)
    {
        return await _orderRepository.GetOrdersByCustomerIdAsync(customerId);
    }

    public async Task ConfirmPreOrderDishAsync(string callerId, string reservationId, string dishId, ILambdaLogger logger)
    {
        var reservation = await LoadReservationAsync(reservationId);

        EnsureCallerIsAssignedWaiter(callerId, reservation);
        EnsurePreOrderEditable(reservation.StartTime);

        var order = await LoadOrderByReservationIdAsync(reservationId);
        bool shouldSubmit = ConfirmDishInOrder(order, dishId);

        if (shouldSubmit)
        {
            order.Status = OrderStatus.Submitted;
            logger.Log($"Order {order.OrderId} marked as Submitted (all dishes confirmed/canceled and at least one confirmed).");
        }

        await _orderRepository.SaveOrderAsync(order);
        logger.Log($"Confirmed dish {dishId} in order {order.OrderId}.");
    }

    public async Task CancelPreOrderDishAsync(string callerId, string reservationId, string dishId, ILambdaLogger logger)
    {
        var reservation = await LoadReservationAsync(reservationId);

        EnsureCallerIsAssignedWaiter(callerId, reservation);
        EnsurePreOrderEditable(reservation.StartTime);

        var order = await LoadOrderByReservationIdAsync(reservationId);

        bool shouldSubmit = CancelDishInOrder(order, dishId);

        if (shouldSubmit)
        {
            order.Status = OrderStatus.Submitted;
            logger.Log($"Order {order.OrderId} marked as Submitted (all dishes confirmed/canceled and at least one confirmed).");
        }

        await _orderRepository.SaveOrderAsync(order);
        logger.Log($"Canceled dish {dishId} in order {order.OrderId}.");
    }

    public async Task<Order?> GetOrderByReservationIdAsync(string reservationId)
    {
        return await _orderRepository.GetOrderByReservationIdAsync(reservationId);
    }

    public async Task AddOrderAsync(Order order)
    {
        var reservation = await LoadReservationAsync(order.ReservationId);
        EnsureReservationReserved(reservation);
        EnsurePreOrderEditable(reservation.StartTime);

        var existingActiveOrder = await GetActiveOrderByReservationIdAsync(order.ReservationId, throwIfNotFound: false);
        if (existingActiveOrder != null)
        {
            throw new ValidationException($"Active order already exists for reservation {order.ReservationId}.");
        }

        order.Status = OrderStatus.Created;
        ResetNewOrderItemStates(order.Dishes);
        await SetPriceSnapshotsAsync(order.Dishes);
        if (!await _orderRepository.TryCreateOrderAsync(order))
            throw new ValidationException($"An order already exists for reservation {order.ReservationId}.");

        if (!string.IsNullOrEmpty(reservation.WaiterId))
        {
            await _notificationService.CreateOrderNotificationAsync(
                reservation.WaiterId,
                order.OrderId,
                order.ReservationId);
        }
    }

    public async Task AddOrderForWaiterAsync(Order order)
    {
        _ = await LoadReservationAsync(order.ReservationId);

        var existingOrder = await GetOrderByReservationIdAsync(order.ReservationId);
        if (existingOrder != null)
        {
            throw new ValidationException($"Active order already exists for reservation {order.ReservationId}.");
        }

        order.Status = OrderStatus.Created;
        ResetNewOrderItemStates(order.Dishes);
        await SetPriceSnapshotsAsync(order.Dishes);
        if (!await _orderRepository.TryCreateOrderAsync(order))
            throw new ValidationException($"An order already exists for reservation {order.ReservationId}.");
    }

    public async Task EditPreOrderAsync(
        string customerId,
        string reservationId,
        List<OrderItem> newDishes,
        ILambdaContext context)
    {
        // A submitted pre-order must not be silently rewritten: the waiter may already be
        // preparing or have confirmed individual dishes, and overwriting them here would wipe
        // that confirmation state without anyone being told. The customer has to explicitly
        // unsubmit first (UnSubmitPreOrderAsync), which already enforces the 30-minute window.
        var existingOrder = await GetOrderByReservationIdAsync(reservationId);
        if (existingOrder is { Status: OrderStatus.SubmittedByCustomer or OrderStatus.Submitted })
            throw new ValidationException("This pre-order was already submitted. Unsubmit it before changing the dishes.");

        await EditPreOrderInternalAsync(
            reservationId,
            newDishes,
            context,
            reservation =>
            {
                if (!string.Equals(reservation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
                    throw new ActionForbiddenException($"User '{customerId}' is not authorized to edit pre-order for reservation '{reservation.Id}' owned by '{reservation.CustomerId}'.");

                if (reservation.Status != ReservationStatus.Reserved)
                    throw new ValidationException($"Pre-order can be edited only if reservation status is 'Reserved'. Current status: '{reservation.Status}'.");

                EnsurePreOrderEditable(reservation.StartTime);
            });
    }

    public async Task EditPreOrderForWaiterAsync(
        string waiterId,
        string reservationId,
        List<OrderItem> newDishes,
        ILambdaContext context)
    {
        await EditPreOrderInternalAsync(
            reservationId,
            newDishes,
            context,
            reservation =>
            {
                if (EqualityHelper.NotEqualsIgnoreCase(reservation.WaiterId, waiterId))
                    throw new ActionForbiddenException($"Waiter '{waiterId}' is not authorized to edit pre-order for reservation '{reservation.Id}' assigned to '{reservation.WaiterId}'.");

                if (reservation.Status != ReservationStatus.InProgress)
                    throw new ValidationException($"Pre-order can be edited only if reservation status is 'InProgress'. Current status: '{reservation.Status}'.");
            });
    }

    public async Task CancelPreOrderAsync(string customerId, string reservationId, ILambdaContext context)
    {
        var reservation = await LoadReservationAsync(reservationId);

        if (!string.Equals(reservation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActionForbiddenException("You are not authorized to cancel this pre-order.");
        }

        EnsureReservationReserved(reservation);
        // Every other pre-order write (edit, confirm, unsubmit) is held to the same 30-minute
        // cutoff; cancelling was the one path that slipped through un-checked.
        EnsurePreOrderEditable(reservation.StartTime);

        var order = await GetActiveOrderByReservationIdAsync(reservationId, throwIfNotFound: true);
        if (order!.Status is not (OrderStatus.Created or OrderStatus.SubmittedByCustomer))
            throw new ValidationException("This pre-order can no longer be cancelled.");
        order.Status = OrderStatus.Cancelled;

        await _orderRepository.SaveOrderAsync(order);
        context.Logger.LogLine($"Pre-order {order.OrderId} for reservation {reservationId} cancelled by customer {customerId}.");
    }

    public async Task SubmitPreOrderAsync(string customerId, string reservationId, ILambdaContext context)
    {
        var reservation = await LoadReservationAsync(reservationId);

        if (!string.Equals(reservation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActionForbiddenException("You are not authorized to submit this pre-order.");
        }

        EnsureReservationReserved(reservation);
        // Every other pre-order write (edit, confirm, unsubmit) is held to the same 30-minute
        // cutoff; submitting was the one path that slipped through un-checked.
        EnsurePreOrderEditable(reservation.StartTime);

        var order = await GetActiveOrderByReservationIdAsync(reservationId, throwIfNotFound: true);
        if (order!.Status != OrderStatus.Created)
            throw new ValidationException("Only a draft pre-order can be submitted.");
        order.Status = OrderStatus.SubmittedByCustomer;

        await _orderRepository.SaveOrderAsync(order);
        context.Logger.LogLine($"Pre-order {order.OrderId} for reservation {reservationId} submitted by customer {customerId}.");
    }

    public async Task UnSubmitPreOrderAsync(string customerId, string reservationId, ILambdaContext context)
    {
        var reservation = await LoadReservationAsync(reservationId);

        if (!string.Equals(reservation.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActionForbiddenException("You are not authorized to submit this pre-order.");
        }

        EnsureReservationReserved(reservation);

        var order = await GetOrderByReservationIdAsync(reservationId);
        if (order == null)
        {
            throw new ValidationException("No pre-order found for the given reservation.");
        }
        if (order.Status != OrderStatus.SubmittedByCustomer)
            throw new ValidationException("Only a customer-submitted pre-order can be unsubmitted.");

        EnsurePreOrderEditable(reservation.StartTime);

        order.Status = OrderStatus.Created;

        await _orderRepository.SaveOrderAsync(order);
        context.Logger.LogLine($"Pre-order {order.OrderId} for reservation {reservationId} submitted by customer {customerId}.");
    }

    private async Task EditPreOrderInternalAsync(
    string reservationId,
    List<OrderItem> newDishes,
    ILambdaContext context,
    Action<Reservation> validateReservation)
    {
        var reservation = await LoadReservationAsync(reservationId);
        validateReservation(reservation);

        var orderItemsForValidation = newDishes.Select(d => new OrderItem
        {
            DishId = d.DishId,
            Quantity = d.Quantity
        }).ToList();

        var isValid = await ValidateDishes(orderItemsForValidation, context);
        if (!isValid)
            throw new ValidationException("One or more dishes in the request are invalid, unavailable, or have a quantity of 0.");

        var order = await GetActiveOrderByReservationIdAsync(reservationId, throwIfNotFound: true);
        order!.Dishes.Clear();

        await SetPriceSnapshotsAsync(newDishes);

        foreach (var dish in newDishes)
        {
            order.Dishes.Add(new OrderItem
            {
                DishId = dish.DishId,
                Quantity = dish.Quantity,
                UnitPriceAtOrder = dish.UnitPriceAtOrder,
                State = OrderItemState.Pending,
            });
        }

        await _orderRepository.SaveOrderAsync(order);
    }

    private async Task SetPriceSnapshotsAsync(IEnumerable<OrderItem> items)
    {
        var orderItems = items.ToList();
        EnsureValidQuantities(orderItems);

        foreach (var item in orderItems)
        {
            var dish = await _dishService.GetDishByIdAsync(item.DishId)
                ?? throw new DishNotFoundException($"Dish {item.DishId} not found.");

            item.UnitPriceAtOrder = dish.Price;
        }
    }

    private static void ResetNewOrderItemStates(IEnumerable<OrderItem> items)
    {
        foreach (var item in items)
            item.State = OrderItemState.Pending;
    }

    private static void EnsureValidQuantities(IEnumerable<OrderItem> items)
    {
        var orderItems = items.ToList();
        if (orderItems.Any(item => item.Quantity < 1 || item.Quantity > OrderItem.MaxQuantityPerDish) ||
            orderItems.GroupBy(item => item.DishId).Any(group => group.Sum(item => (long)item.Quantity) > OrderItem.MaxQuantityPerDish))
        {
            throw new ValidationException($"Each dish quantity must be between 1 and {OrderItem.MaxQuantityPerDish}.");
        }
    }

    private async Task<Order?> GetActiveOrderByReservationIdAsync(string reservationId, bool throwIfNotFound = true)
    {
        var order = await _orderRepository.GetOrderByReservationIdAsync(reservationId);

        if (order == null && throwIfNotFound)
        {
            throw new ValidationException($"Active pre-order for reservation '{reservationId}' not found.");
        }

        return order;
    }

    private async Task<Reservation> LoadReservationAsync(string reservationId)
    {
        var reservation = await _reservationRepository.GetReservationByIdAsync(reservationId);
        return reservation ?? throw new ReservationNotFoundException("Reservation not found.");
    }

    private async Task<Order> LoadOrderByReservationIdAsync(string reservationId)
    {
        var order = await _orderRepository.GetOrderByReservationIdAsync(reservationId);

        return order ?? throw new ValidationException("Order not found.");
    }

    private static void EnsurePreOrderEditable(DateTime reservationStartTime)
    {
        var cutoff = reservationStartTime.ToUniversalTime().AddMinutes(-30);
        if (DateTime.UtcNow > cutoff)
            throw new ValidationException($"Pre-orders can only be modified up to 30 minutes before the reservation start time ({reservationStartTime:HH:mm}).");
    }

    private static void EnsureReservationReserved(Reservation reservation)
    {
        if (reservation.Status != ReservationStatus.Reserved)
            throw new ValidationException($"Customer pre-orders are only available for Reserved reservations. Current status: '{reservation.Status}'.");
    }

    private static bool ConfirmDishInOrder(Order order, string dishId)
    {
        var dish = LoadPendingDish(order, dishId);

        dish.State = OrderItemState.Confirmed;

        return ShouldMarkOrderAsSubmitted(order);
    }

    private static bool CancelDishInOrder(Order order, string dishId)
    {
        var dish = LoadPendingDish(order, dishId);

        dish.State = OrderItemState.Canceled;

        return ShouldMarkOrderAsSubmitted(order);
    }

    private static OrderItem LoadPendingDish(Order order, string dishId)
    {
        if (order.Status is OrderStatus.Submitted or OrderStatus.Cancelled)
            throw new ValidationException($"Order '{order.OrderId}' can no longer be changed.");

        var dish = order.Dishes.FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in order {order.OrderId}.");

        if (dish.State != OrderItemState.Pending)
            throw new ValidationException($"Dish '{dishId}' has already been processed.");

        return dish;
    }

    private static void EnsureCallerIsAssignedWaiter(string callerId, Reservation reservation)
    {
        if (DataValidationHelper.IsMissing(reservation.WaiterId))
        {
            throw new ValidationException($"Reservation '{reservation.Id}' has no assigned waiter.");
        }

        if (!string.Equals(reservation.WaiterId, callerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActionForbiddenException($"Action forbidden: You '{callerId}' are not the assigned waiter '{reservation.WaiterId}' for reservation '{reservation.Id}'.");
        }
    }

    private async Task<bool> ValidateDishes(List<OrderItem> orderItems, ILambdaContext context)
    {
        try
        {
            EnsureValidQuantities(orderItems);
        }
        catch (ValidationException)
        {
            return false;
        }

        foreach (var item in orderItems)
        {
            if (string.IsNullOrEmpty(item.DishId) || item.Quantity <= 0)
            {
                context.Logger.LogLine($"Invalid dish ID or quantity for dish: {item.DishId}, quantity: {item.Quantity}");
                return false;
            }

            var dish = await _dishService.GetDishByIdAsync(item.DishId);
            if (dish is null || dish.State != DishState.Available)
            {
                context.Logger.LogLine($"Dish with ID {item.DishId} is not available.");
                return false;
            }
        }
        return orderItems.Count > 0;
    }

    private static bool ShouldMarkOrderAsSubmitted(Order order)
    {
        bool anyConfirmed = order.Dishes.Any(d => d.State == OrderItemState.Confirmed);
        bool anyPending = order.Dishes.Any(d => d.State == OrderItemState.Pending);

        return anyConfirmed && !anyPending;
    }
}