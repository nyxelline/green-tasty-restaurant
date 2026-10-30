using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace RestaurantAqa.Tests.Tests.Ui;

/// <summary>
/// Common base for browser tests. Microsoft.Playwright.NUnit itself (not this class) decides
/// headless/headed and which browser to launch, from environment variables - see the README
/// ("HEADED=1" to watch the browser, "BROWSER=firefox"/"webkit" to switch engine). This class
/// only fixes the viewport and TLS handling that every test in this suite wants.
/// </summary>
public abstract class UiTestBase : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        IgnoreHTTPSErrors = true
    };
}
