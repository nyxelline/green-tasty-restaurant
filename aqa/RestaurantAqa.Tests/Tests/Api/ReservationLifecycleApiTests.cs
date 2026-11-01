using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using RestaurantAqa.Tests.Api;
using RestaurantAqa.Tests.Config;

namespace RestaurantAqa.Tests.Tests.Api;

/// <summary>
/// Covers the reservation lifecycle (cancel/edit) that the group pre-order tests don't touch:
/// who is and isn't allowed to cancel or edit a reservation, and what happens to an already-
/// cancelled one. Added to round out the API-level negative-path coverage - the original AQA
/// homework only scoped the group pre-order "view participants" endpoint (see
/// GroupSessionsApiTests), these extend the same API-client/NUnit style to a second area.
/// </summary>
[TestFixture]
[Category("Api")]
public class ReservationLifecycleApiTests
{
    private ApiClient _api = null!;

    [SetUp]
    public void SetUp() => _api = new ApiClient();

    [TearDown]
    public void TearDown() => _api.Dispose();

    [Test(Description = "The reservation's own owner can cancel it.")]
    public async Task Owner_CanCancelTheirOwnReservation()
    {
        var (_, ownerToken, reservationId) = await CreateReservationAsNewCustomerAsync();

        var response = await _api.CancelReservationRawAsync(reservationId, ownerToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test(Description = "A different Customer's token cannot cancel someone else's reservation.")]
    public async Task UnrelatedCustomer_CannotCancelSomeoneElsesReservation()
    {
        var (_, _, reservationId) = await CreateReservationAsNewCustomerAsync();
        var (_, intruderToken) = await _api.RegisterAndSignInNewCustomerAsync("cancel-intruder");

        var response = await _api.CancelReservationRawAsync(reservationId, intruderToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test(Description = "Cancelling with no Authorization header at all is rejected as unauthenticated.")]
    public async Task NoBearerToken_CancelIsRejectedAsUnauthorized()
    {
        var (_, _, reservationId) = await CreateReservationAsNewCustomerAsync();

        var response = await _api.CancelReservationRawAsync(reservationId, idToken: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test(Description = "Cancelling a reservation id that doesn't exist returns Not Found, not a silent success.")]
    public async Task CancellingNonexistentReservation_ReturnsNotFound()
    {
        var (_, ownerToken) = await _api.RegisterAndSignInNewCustomerAsync("cancel-ghost");
        var madeUpReservationId = Guid.NewGuid().ToString();

        var response = await _api.CancelReservationRawAsync(madeUpReservationId, ownerToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test(Description = "A different Customer's token cannot edit someone else's reservation.")]
    public async Task UnrelatedCustomer_CannotEditSomeoneElsesReservation()
    {
        var (_, _, reservationId) = await CreateReservationAsNewCustomerAsync();
        var (_, intruderToken) = await _api.RegisterAndSignInNewCustomerAsync("edit-intruder");

        var newStart = DateTime.UtcNow.AddDays(10);
        var response = await _api.EditReservationRawAsync(reservationId, newStart, newStart.AddHours(1), 2, intruderToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test(Description = "Once cancelled, a reservation can't be edited back to life through the edit endpoint.")]
    public async Task EditingAnAlreadyCancelledReservation_IsRejected()
    {
        var (_, ownerToken, reservationId) = await CreateReservationAsNewCustomerAsync();
        var cancelResponse = await _api.CancelReservationRawAsync(reservationId, ownerToken);
        Assert.That(cancelResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Precondition: cancel must succeed first.");

        var newStart = DateTime.UtcNow.AddDays(10);
        var response = await _api.EditReservationRawAsync(reservationId, newStart, newStart.AddHours(1), 2, ownerToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// Registers a new Customer and books them a standalone (non-group) reservation a safely far
    /// future slot out, retrying on the real backend's table-overlap conflict - same approach as
    /// GroupPreOrderScenario.CreateAsync, kept separate here since this file doesn't need a group
    /// reservation/invite at all, just a plain one to cancel/edit.
    /// </summary>
    private async Task<(string Email, string IdToken, string ReservationId)> CreateReservationAsNewCustomerAsync()
    {
        var (email, idToken) = await _api.RegisterAndSignInNewCustomerAsync("lifecycle");

        const int maxAttempts = 6;
        Exception? lastOverlapError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var seed = (uint)Guid.NewGuid().GetHashCode();
            var dayOffset = 2 + (int)(seed % 1400);
            var hour = 12 + (int)((seed / 1400) % 8);
            var startTime = DateTime.UtcNow.Date.AddDays(dayOffset).AddHours(hour);
            var endTime = startTime.AddHours(2);

            try
            {
                var tableId = await _api.FindAvailableTableIdAsync(TestConfig.LocationId, capacity: 2, startTime, idToken);
                var reservationId = await _api.CreateReservationAsync(tableId, startTime, endTime, guestCount: 2, idToken);
                return (email, idToken, reservationId);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("overlaps with an existing reservation"))
            {
                lastOverlapError = ex;
            }
        }
        throw new InvalidOperationException($"Could not find a free slot after {maxAttempts} attempts.", lastOverlapError);
    }
}
