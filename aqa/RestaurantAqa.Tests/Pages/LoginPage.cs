using System.Threading.Tasks;
using Microsoft.Playwright;
using RestaurantAqa.Tests.Config;

namespace RestaurantAqa.Tests.Pages;

/// <summary>Owner/customer sign-in. The app renders this as a modal over the main page at "/login".</summary>
public sealed class LoginPage(IPage page)
{
    public async Task GotoAsync()
        => await page.GotoAsync($"{TestConfig.FrontendUrl.TrimEnd('/')}/login");

    public async Task SignInAsync(string email, string password)
    {
        var emailInput = page.GetByPlaceholder("e.g. username@domain.com");
        await emailInput.FillAsync(email);
        await page.Locator("label:has-text('Password') input").FillAsync(password);
        // Exact match: the modal's close control is also an accessible "button" whose name
        // ("Close Sign In to Your Account") contains "Sign In", so a non-exact match is ambiguous.
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }).ClickAsync();

        // The click only dispatches the sign-in call; wait for it to actually succeed (the modal/
        // form disappears) before treating the user as logged in and navigating elsewhere.
        await emailInput.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15000 });
    }
}
