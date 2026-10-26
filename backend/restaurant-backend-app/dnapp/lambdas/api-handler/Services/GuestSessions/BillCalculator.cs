using APIHandler.Models.GuestSessions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace APIHandler.Services.GuestSessions;

/// Splits a group's pre-order into per-person bills: everybody pays for what he or she ordered.
public static class BillCalculator
{
    public static GroupBillResponse Build(string groupReservationId, IEnumerable<Participant> participants)
    {
        var guestBills = participants
            .OrderBy(participant => participant.Type == ParticipantType.Owner ? 0 : 1)
            .ThenBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToGuestBill)
            .ToList();

        decimal total = guestBills.Sum(bill => bill.Subtotal);

        foreach (var bill in guestBills)
        {
            bill.SharePercent = total > 0
                ? Math.Round(bill.Subtotal / total * 100m, 1, MidpointRounding.AwayFromZero)
                : 0m;
        }

        return new GroupBillResponse
        {
            GroupReservationId = groupReservationId,
            Guests = guestBills,
            Total = total
        };
    }

    private static GuestBillResponse ToGuestBill(Participant participant)
    {
        var lines = participant.Items
            .Select(item => new BillLineResponse
            {
                DishId = item.Dish.Id,
                Name = item.Dish.Name,
                UnitPrice = item.Dish.Price,
                Quantity = item.Quantity,
                LineTotal = item.Dish.Price * item.Quantity
            })
            .ToList();

        return new GuestBillResponse
        {
            GuestSessionId = participant.Id,
            Name = participant.Name,
            IsOwner = participant.Type == ParticipantType.Owner,
            IsSubmitted = participant.IsSubmitted,
            Lines = lines,
            Subtotal = lines.Sum(line => line.LineTotal)
        };
    }
}
