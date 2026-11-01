using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using RestaurantAqa.Tests.Api;

namespace RestaurantAqa.Tests.Tests.Api;

/// <summary>
/// Sign-in negative paths that aren't exercised anywhere else: a real account with the wrong
/// password, an email nobody registered, and a garbage bearer token on an endpoint that
/// requires one. Kept as its own small file rather than folding into the group pre-order or
/// reservation-lifecycle test classes, since it's not really about either feature - it's about
/// the auth layer itself.
/// </summary>
[TestFixture]
[Category("Api")]
public class AuthNegativeApiTests
{
    private ApiClient _api = null!;

    [SetUp]
    public void SetUp() => _api = new ApiClient();

    [TearDown]
    public void TearDown() => _api.Dispose();

    [Test(Description = "Signing in with the wrong password for a real, registered account is rejected.")]
    public async Task SignIn_WithWrongPasswordForRealAccount_ReturnsUnauthorized()
    {
        var (email, _) = await _api.RegisterAndSignInNewCustomerAsync("wrong-pwd");

        var ex = Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(
            async () => await _api.SignInAsync(email, "Definitely-Not-The-Real-Password1"));
        Assert.That(ex!.Message, Does.Contain("401"));
    }

    [Test(Description = "Signing in with an email nobody has ever registered is rejected the same way as a wrong password - it shouldn't reveal whether the account exists.")]
    public void SignIn_WithUnregisteredEmail_ReturnsUnauthorized()
    {
        var unregisteredEmail = $"aqa.nobody.{System.Guid.NewGuid():N}@aqatests.com";

        var ex = Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(
            async () => await _api.SignInAsync(unregisteredEmail, "Whatever-Password1"));
        Assert.That(ex!.Message, Does.Contain("401"));
    }

    [Test(Description = "A garbage (not a real JWT) bearer token on an authenticated endpoint is rejected, not silently treated as unauthenticated-OK or crashing the handler.")]
    public async Task GarbageBearerToken_OnAuthenticatedEndpoint_ReturnsUnauthorized()
    {
        var response = await _api.GetRawAsync("reservations", bearerToken: "this-is-not-a-real-jwt-token");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
