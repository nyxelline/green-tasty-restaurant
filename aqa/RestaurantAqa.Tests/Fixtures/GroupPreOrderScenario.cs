using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using RestaurantAqa.Tests.Api;
using RestaurantAqa.Tests.Config;

namespace RestaurantAqa.Tests.Fixtures;

/// <summary>
/// Builds a fresh, isolated "group pre-order" scenario against the real backend for each test:
/// a brand-new Owner, a brand-new reservation at a real location, and an invite link/group id
/// that guests can join through the guest-session API. Every run uses a unique email and a
/// unique future time slot, so tests are independent and can run in parallel without clashing
/// with each other or with real data.
/// </summary>
public sealed class GroupPreOrderScenario : IAsyncDisposable
{
    public ApiClient Api { get; }
    public string OwnerEmail { get; private set; } = string.Empty;
    public string OwnerIdToken { get; private set; } = string.Empty;
    public string ReservationId { get; private set; } = string.Empty;
    public string GroupReservationId { get; private set; } = string.Empty;
    public string InviteToken { get; private set; } = string.Empty;
    public string LocationId { get; }

    private string? _cachedDishId;
    private readonly List<(string SessionId, string SessionToken, string Name)> _guests = [];

    private GroupPreOrderScenario(string locationId)
    {
        Api = new ApiClient();
        LocationId = locationId;
    }

    /// <summary>
    /// Registers a new Owner, books a table for `guestCount` people at a unique future time slot,
    /// and opens the group invite (so a GroupReservation exists for it on the backend).
    /// </summary>
    public static async Task<GroupPreOrderScenario> CreateAsync(int guestCount = 3)
    {
        var scenario = new GroupPreOrderScenario(TestConfig.LocationId);

        var (email, idToken) = await scenario.Api.RegisterAndSignInNewCustomerAsync("owner");
        scenario.OwnerEmail = email;
        scenario.OwnerIdToken = idToken;

        // Pick a random time slot and book it. There's no cleanup API for reservations, so past
        // test runs' bookings pile up on the same few tables over time; rather than trying to pick
        // a slot that's "surely" free, retry on the specific "overlaps with an existing
        // reservation" conflict the real backend reports, which is robust regardless of how many
        // past runs came before this one.
        const int maxAttempts = 6;
        Exception? lastOverlapError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // A fresh random slot each attempt: a few days to ~4 years out, hour picked from a
            // 12:00-19:00 band so a 2-hour reservation always finishes before the backend's
            // 22:00-same-day cutoff.
            var seed = (uint)Guid.NewGuid().GetHashCode();
            var dayOffset = 2 + (int)(seed % 1400);
            var hour = 12 + (int)((seed / 1400) % 8);
            var startTime = DateTime.UtcNow.Date.AddDays(dayOffset).AddHours(hour);
            var endTime = startTime.AddHours(2);

            try
            {
                var tableId = await scenario.Api.FindAvailableTableIdAsync(scenario.LocationId, guestCount, startTime, idToken);
                scenario.ReservationId = await scenario.Api.CreateReservationAsync(tableId, startTime, endTime, guestCount, idToken);
                lastOverlapError = null;
                break;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("overlaps with an existing reservation"))
            {
                lastOverlapError = ex;
            }
        }
        if (lastOverlapError is not null)
            throw new InvalidOperationException($"Could not find a free slot after {maxAttempts} attempts.", lastOverlapError);

        var invite = await scenario.Api.GetInviteTokenAsync(scenario.ReservationId, idToken);
        scenario.InviteToken = invite.Token;
        scenario.GroupReservationId = invite.GroupReservationId
            ?? throw new InvalidOperationException("Invite token response did not include a groupReservationId.");

        return scenario;
    }

    /// <summary>Joins the group as a new guest. Optionally adds a real selectable dish and submits their cart.</summary>
    public async Task<(string SessionId, string SessionToken, string Name)> AddGuestAsync(
        string? name = null, bool addDish = false, int quantity = 1, bool submit = false)
    {
        name ??= $"Guest-{Guid.NewGuid():N}"[..12];
        var session = await Api.CreateGuestSessionAsync(GroupReservationId, name);

        if (addDish)
        {
            var dishId = await GetOrPickDishIdAsync(session.SessionId, session.SessionToken);
            await Api.AddDishToGuestSessionAsync(session.SessionId, session.SessionToken, dishId, quantity);
        }

        if (submit)
            await Api.SubmitGuestCartAsync(session.SessionId, session.SessionToken);

        var guest = (session.SessionId, session.SessionToken, name);
        _guests.Add(guest);
        return guest;
    }

    private async Task<string> GetOrPickDishIdAsync(string guestSessionId, string sessionToken)
    {
        _cachedDishId ??= await Api.FindSelectableDishIdFromGuestMenuAsync(guestSessionId, sessionToken)
            ?? throw new InvalidOperationException($"No selectable dish found on the guest menu for location {LocationId}.");
        return _cachedDishId;
    }

    public ValueTask DisposeAsync()
    {
        Api.Dispose();
        return ValueTask.CompletedTask;
    }
}
