using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using RestaurantAqa.Tests.Config;

namespace RestaurantAqa.Tests.Api;

/// <summary>
/// Thin wrapper over the real backend REST API (API Gateway + Lambda), used by both the API-level
/// tests and the UI tests' data setup (seeding owner/guests/dishes via API is far faster and more
/// reliable than driving the whole flow through the browser every time).
/// </summary>
public sealed class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public ApiClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(TestConfig.ApiBaseUrl.TrimEnd('/') + "/") };
    }

    /// <summary>Raw call that returns the HttpResponseMessage as-is, for tests asserting on status codes.</summary>
    public async Task<HttpResponseMessage> GetRawAsync(string path, string? bearerToken = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearerToken is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await _http.SendAsync(req);
    }

    public async Task<string> RegisterAsync(string firstName, string lastName, string email, string password)
    {
        var body = new
        {
            firstName,
            lastName,
            email,
            password,
            confirmPassword = password
        };
        var response = await _http.PostAsJsonAsync("register", body);
        await EnsureSuccessWithBodyAsync(response);
        return email;
    }

    public async Task<string> SignInAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("signin", new { email, password });
        await EnsureSuccessWithBodyAsync(response);
        var dto = await response.Content.ReadFromJsonAsync<SignInResponseDto>(JsonOptions);
        return dto?.IdToken ?? throw new InvalidOperationException("Sign-in succeeded but no idToken was returned.");
    }

    /// <summary>Registers a brand-new, uniquely-named Customer and signs them in. Returns their bearer token.</summary>
    public async Task<(string Email, string IdToken)> RegisterAndSignInNewCustomerAsync(string namePrefix)
    {
        var unique = Guid.NewGuid().ToString("N")[..10];
        // ".com" (2-3 char TLD segment) to satisfy the backend's own email regex
        // (^([\w.\-]+)@([\w\-]+)((\.(\w){2,3})+)$) - a longer fake TLD like ".local" fails it with a 400.
        var email = $"aqa.{namePrefix}.{unique}@aqatests.com".ToLowerInvariant();
        const string password = "Aqa#Test12";
        await RegisterAsync(namePrefix, "Tester", email, password);
        var idToken = await SignInAsync(email, password);
        return (email, idToken);
    }

    public async Task<string> FindAvailableTableIdAsync(string locationId, int capacity, DateTime startTimeUtc, string idToken)
    {
        var url = $"available-tables?locationId={Uri.EscapeDataString(locationId)}&capacity={capacity}" +
                  $"&startTime={Uri.EscapeDataString(startTimeUtc.ToString("O"))}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        var response = await _http.SendAsync(req);
        await EnsureSuccessWithBodyAsync(response);
        var tables = await response.Content.ReadFromJsonAsync<List<AvailableTableDto>>(JsonOptions) ?? [];
        var table = tables.FirstOrDefault()
            ?? throw new InvalidOperationException($"No available table found for location {locationId} at {startTimeUtc:O}.");
        return table.Id;
    }

    public async Task<string> CreateReservationAsync(string tableId, DateTime startTimeUtc, DateTime endTimeUtc, int guestCount, string idToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "reservations")
        {
            Content = JsonContent.Create(new
            {
                tableId,
                startTime = startTimeUtc,
                endTime = endTimeUtc,
                guestCount
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        var response = await _http.SendAsync(req);
        await EnsureSuccessWithBodyAsync(response);
        var dto = await response.Content.ReadFromJsonAsync<CreateReservationResponseDto>(JsonOptions);
        return dto?.Id ?? throw new InvalidOperationException("Reservation was created but no id was returned.");
    }

    public async Task<InviteTokenDto> GetInviteTokenAsync(string reservationId, string idToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"reservations/{reservationId}/invite-token");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        var response = await _http.SendAsync(req);
        await EnsureSuccessWithBodyAsync(response);
        return await response.Content.ReadFromJsonAsync<InviteTokenDto>(JsonOptions)
            ?? throw new InvalidOperationException("Invite-token response body was empty.");
    }

    public async Task<CreateGuestSessionResponseDto> CreateGuestSessionAsync(string groupReservationId, string guestName)
    {
        var response = await _http.PostAsJsonAsync("guest/sessions", new { groupReservationId, guestName });
        await EnsureSuccessWithBodyAsync(response);
        return await response.Content.ReadFromJsonAsync<CreateGuestSessionResponseDto>(JsonOptions)
            ?? throw new InvalidOperationException("Guest session response body was empty.");
    }

    public async Task AddDishToGuestSessionAsync(string guestSessionId, string sessionToken, string dishId, int quantity)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"guest/sessions/{guestSessionId}/dishes")
        {
            Content = JsonContent.Create(new { dishId, quantity })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        var response = await _http.SendAsync(req);
        await EnsureSuccessWithBodyAsync(response);
    }

    public async Task SubmitGuestCartAsync(string guestSessionId, string sessionToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"guest/sessions/{guestSessionId}/submit")
        {
            Content = JsonContent.Create(new { })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        var response = await _http.SendAsync(req);
        await EnsureSuccessWithBodyAsync(response);
    }

    /// <summary>
    /// Looks at a guest's own menu (GET /guest/sessions/{id}/menu) and returns the id of the first
    /// dish marked "selectable" (i.e. actually orderable right now). The menu is location-wide, not
    /// guest-specific, so any one guest's session can be used to discover a dish id to seed carts with.
    /// Parsed as raw JSON rather than a strongly-typed dictionary because the outer object's keys are
    /// dish-type enum values whose exact wire casing isn't worth pinning down for this purpose.
    /// </summary>
    public async Task<string?> FindSelectableDishIdFromGuestMenuAsync(string guestSessionId, string sessionToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"guest/sessions/{guestSessionId}/menu");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        var response = await _http.SendAsync(req);
        await EnsureSuccessWithBodyAsync(response);

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var dishesProperty = doc!.RootElement.EnumerateObject()
            .FirstOrDefault(p => p.Name.Equals("dishes", StringComparison.OrdinalIgnoreCase));

        if (dishesProperty.Value.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var categoryProperty in dishesProperty.Value.EnumerateObject())
        {
            foreach (var dish in categoryProperty.Value.EnumerateArray())
            {
                var selectable = dish.TryGetProperty("selectable", out var selectableEl) && selectableEl.ValueKind == JsonValueKind.True;
                if (!selectable) continue;

                if (dish.TryGetProperty("id", out var idEl))
                    return idEl.GetString();
            }
        }
        return null;
    }

    /// <summary>The L1 "view participants" endpoint itself, exposed raw so tests can assert status codes too.</summary>
    public Task<HttpResponseMessage> GetGroupSessionsRawAsync(string groupReservationId, string? idToken)
        => GetRawAsync($"guest/groups/{groupReservationId}/sessions", idToken);

    public async Task<GroupSessionsResponseDto> GetGroupSessionsAsync(string groupReservationId, string idToken)
    {
        var response = await GetGroupSessionsRawAsync(groupReservationId, idToken);
        await EnsureSuccessWithBodyAsync(response);
        return await response.Content.ReadFromJsonAsync<GroupSessionsResponseDto>(JsonOptions)
            ?? throw new InvalidOperationException("Group-sessions response body was empty.");
    }

    private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"{(int)response.StatusCode} {response.ReasonPhrase} calling {response.RequestMessage?.Method} " +
            $"{response.RequestMessage?.RequestUri}: {body}");
    }

    public void Dispose() => _http.Dispose();
}
