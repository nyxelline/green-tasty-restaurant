using System.Threading.Tasks;
using Microsoft.Playwright;
using RestaurantAqa.Tests.Config;

namespace RestaurantAqa.Tests.Pages;

/// <summary>
/// The Owner's "Group pre-order" page (L1's core UI): lists every guest who joined, their
/// dishes, per-guest totals and the running table total.
/// </summary>
public sealed class GroupOrderPage(IPage page)
{
    private ILocator GuestCards => page.Locator(".guest");
    private ILocator EmptyState => page.Locator(".empty");
    private ILocator GuestsSectionTitle => page.Locator(".section__title", new() { HasText = "Guests' pre-orders" });

    public async Task GotoAsync(string reservationId)
        => await page.GotoAsync($"{TestConfig.FrontendUrl.TrimEnd('/')}/group-order/{reservationId}");

    /// <summary>
    /// Waits for the page to actually finish loading data, not just for its (always-rendered)
    /// title. ".group-order__title" is outside the @switch on load state and appears during
    /// 'loading' too, so waiting on it alone races with the data fetch; ".cart" only renders once
    /// the page reaches its 'ready' state.
    /// </summary>
    public async Task WaitUntilLoadedAsync()
        => await page.Locator(".cart").WaitForAsync(new() { Timeout = 15000 });

    public Task<int> GetGuestCountAsync() => GuestCards.CountAsync();

    public async Task<string> GetGuestsSectionHeadingAsync() => await GuestsSectionTitle.InnerTextAsync();

    public async Task<bool> IsEmptyStateShownAsync() => await EmptyState.CountAsync() > 0;

    private ILocator GuestCardByName(string guestName)
        => page.Locator(".guest").Filter(new() { Has = page.Locator(".guest__name", new() { HasText = guestName }) });

    public async Task<bool> HasGuestAsync(string guestName) => await GuestCardByName(guestName).CountAsync() > 0;

    public async Task<string> GetGuestStatusAsync(string guestName)
        => await GuestCardByName(guestName).Locator(".badge").First.InnerTextAsync();

    public async Task<string> GetGuestTotalTextAsync(string guestName)
        => await GuestCardByName(guestName).Locator(".guest__total").InnerTextAsync();

    public async Task<string> GetTableTotalTextAsync()
        => await page.Locator(".total__value").InnerTextAsync();
}
