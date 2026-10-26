using APIHandler.Models.GuestSessions;
using APIHandler.Models.Dishes;
using APIHandler.Models.Orders;
using APIHandler.Services.GuestSessions;

namespace APIHandler.Tests.Services;

public class BillCalculatorTests
{
    private static OrderItemResponse Item(string id, string name, decimal price, int quantity) => new()
    {
        Dish = new DishResponse
        {
            Id = id, Name = name, Description = "", DishType = "MainCourse", Weight = "", Calories = "",
            Proteins = "", Fats = "", Carbohydrates = "", Vitamins = "", ImageUrl = null, State = "Available",
            Price = price
        },
        Quantity = quantity
    };

    private static Participant Person(string id, ParticipantType type, string name, bool submitted, params OrderItemResponse[] items) =>
        new(id, type, name, items, items.Sum(i => i.Quantity * i.Dish.Price), submitted);

    [Fact]
    public void Build_EachPersonPaysForOwnDishes_SubtotalsAndTotalAddUp()
    {
        var owner = Person("o", ParticipantType.Owner, "Mia", true, Item("d1", "Risotto", 19m, 1));
        var guest = Person("g", ParticipantType.Guest, "Alex", true, Item("d2", "Salmon", 27m, 1), Item("d1", "Risotto", 19m, 2));

        var bill = BillCalculator.Build("group-1", [guest, owner]);

        Assert.Equal("group-1", bill.GroupReservationId);
        Assert.Equal(2, bill.Guests.Count);
        Assert.Equal(19m, bill.Guests[0].Subtotal);
        Assert.Equal(65m, bill.Guests[1].Subtotal);
        Assert.Equal(84m, bill.Total);
        Assert.Equal(bill.Guests.Sum(g => g.Subtotal), bill.Total);
    }

    [Fact]
    public void Build_OwnerIsListedFirst_ThenGuestsByName()
    {
        var bob = Person("b", ParticipantType.Guest, "Bob", true, Item("d1", "Soup", 9m, 1));
        var anna = Person("a", ParticipantType.Guest, "anna", true, Item("d1", "Soup", 9m, 1));
        var owner = Person("o", ParticipantType.Owner, "Zed", true, Item("d1", "Soup", 9m, 1));

        var bill = BillCalculator.Build("g", [bob, anna, owner]);

        Assert.Equal(new[] { "Zed", "anna", "Bob" }, bill.Guests.Select(g => g.Name).ToArray());
        Assert.True(bill.Guests[0].IsOwner);
        Assert.False(bill.Guests[1].IsOwner);
    }

    [Fact]
    public void Build_LinesCarryUnitPriceQuantityAndLineTotal()
    {
        var guest = Person("g", ParticipantType.Guest, "Alex", false, Item("d9", "Fondant", 9.50m, 3));

        var bill = BillCalculator.Build("g", [guest]);

        var line = Assert.Single(bill.Guests[0].Lines);
        Assert.Equal("d9", line.DishId);
        Assert.Equal("Fondant", line.Name);
        Assert.Equal(9.50m, line.UnitPrice);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(28.50m, line.LineTotal);
        Assert.False(bill.Guests[0].IsSubmitted);
    }

    [Fact]
    public void Build_SharePercentIsRoundedToOneDecimal()
    {
        var first = Person("a", ParticipantType.Guest, "A", true, Item("d", "X", 10m, 1));
        var second = Person("b", ParticipantType.Guest, "B", true, Item("d", "X", 20m, 1));

        var bill = BillCalculator.Build("g", [first, second]);

        Assert.Equal(33.3m, bill.Guests[0].SharePercent);
        Assert.Equal(66.7m, bill.Guests[1].SharePercent);
    }

    [Fact]
    public void Build_NobodyOrderedAnything_TotalAndSharesAreZero()
    {
        var owner = Person("o", ParticipantType.Owner, "Mia", false);
        var guest = Person("g", ParticipantType.Guest, "Alex", false);

        var bill = BillCalculator.Build("g", [owner, guest]);

        Assert.Equal(0m, bill.Total);
        Assert.All(bill.Guests, g => Assert.Equal(0m, g.SharePercent));
        Assert.All(bill.Guests, g => Assert.Empty(g.Lines));
    }

    [Fact]
    public void Build_NoParticipants_ReturnsEmptyBill()
    {
        var bill = BillCalculator.Build("g", []);

        Assert.Empty(bill.Guests);
        Assert.Equal(0m, bill.Total);
    }
}
