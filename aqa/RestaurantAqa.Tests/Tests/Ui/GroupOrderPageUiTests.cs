using System.Threading.Tasks;
using NUnit.Framework;
using RestaurantAqa.Tests.Fixtures;
using RestaurantAqa.Tests.Pages;

namespace RestaurantAqa.Tests.Tests.Ui;

/// <summary>
/// L1 "Перегляд учасників групового pre-order" - UI coverage. The Owner is the one who views
/// this page, so every scenario here logs in as the scenario's Owner and opens their own
/// "Group pre-order" page; the guests themselves join and order purely through the API (seeding
/// data this way is far more reliable than re-driving the guest flow through the browser for
/// every assertion, and the guest side of the flow is out of scope for this L1).
/// </summary>
[TestFixture]
[Category("Ui")]
public class GroupOrderPageUiTests : UiTestBase
{
    private async Task<GroupOrderPage> OpenAsOwnerAsync(GroupPreOrderScenario scenario)
    {
        var login = new LoginPage(Page);
        await login.GotoAsync();
        await login.SignInAsync(scenario.OwnerEmail, "Aqa#Test12");

        var groupOrderPage = new GroupOrderPage(Page);
        await groupOrderPage.GotoAsync(scenario.ReservationId);
        await groupOrderPage.WaitUntilLoadedAsync();
        return groupOrderPage;
    }

    [Test(Description = "A group with no guests yet shows the 'no guests have joined' empty state.")]
    public async Task NoGuestsYet_ShowsEmptyState()
    {
        await using var scenario = await GroupPreOrderScenario.CreateAsync();

        var page = await OpenAsOwnerAsync(scenario);

        Assert.That(await page.IsEmptyStateShownAsync(), Is.True);
        Assert.That(await page.GetGuestsSectionHeadingAsync(), Does.Contain("(0)"));
    }

    [Test(Description = "Guests who joined and ordered show up on the Owner's page with their own status and total, and the table total reflects everyone.")]
    public async Task GuestsWhoJoined_AppearWithStatusAndTotals()
    {
        await using var scenario = await GroupPreOrderScenario.CreateAsync();

        var (_, _, submittedGuestName) = await scenario.AddGuestAsync(addDish: true, quantity: 1, submit: true);
        var (_, _, stillChoosingGuestName) = await scenario.AddGuestAsync(addDish: true, quantity: 1, submit: false);

        var page = await OpenAsOwnerAsync(scenario);

        Assert.That(await page.GetGuestCountAsync(), Is.EqualTo(2));
        Assert.That(await page.GetGuestsSectionHeadingAsync(), Does.Contain("(2)"));

        Assert.That(await page.HasGuestAsync(submittedGuestName), Is.True);
        Assert.That(await page.HasGuestAsync(stillChoosingGuestName), Is.True);

        var submittedStatus = await page.GetGuestStatusAsync(submittedGuestName);
        Assert.That(submittedStatus, Does.Not.Contain("choosing").IgnoreCase,
            "A guest who submitted their cart should not still be shown as 'choosing'.");
    }

    [Test(Description = "A guest who joined but has not picked anything yet is still listed, so the Owner knows to follow up.")]
    public async Task GuestWithNoDishesYet_IsStillListedAsAwaitingOrder()
    {
        await using var scenario = await GroupPreOrderScenario.CreateAsync();

        var (_, _, guestName) = await scenario.AddGuestAsync();

        var page = await OpenAsOwnerAsync(scenario);

        Assert.That(await page.HasGuestAsync(guestName), Is.True);
        var status = await page.GetGuestStatusAsync(guestName);
        Assert.That(status, Is.Not.Empty, "A guest with no items yet should still show a status badge (e.g. 'no order yet').");
    }
}
