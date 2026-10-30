using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using RestaurantAqa.Tests.Api;
using RestaurantAqa.Tests.Fixtures;

namespace RestaurantAqa.Tests.Tests.Api;

/// <summary>
/// L1 "Перегляд учасників групового pre-order" - API-level coverage for
/// GET /guest/groups/{groupReservationId}/sessions, the endpoint the Owner's UI calls to
/// list group participants, their dishes and totals.
///
/// Scope (Full L1, per the homework): auth/validation negative cases here at the API level
/// (faster and more precise than driving them through the browser), data setup for the UI
/// scenarios via this same API, and the positive "shape of the data" checks that don't need
/// a browser.
/// </summary>
[TestFixture]
[Category("Api")]
public class GroupSessionsApiTests
{
    [Test(Description = "A newly-created group with no guests yet lists only the Owner (no guest participants) and a total of 0.")]
    public async Task EmptyGroup_ListsOnlyOwnerWithZeroTotal()
    {
        await using var scenario = await GroupPreOrderScenario.CreateAsync();

        var result = await scenario.Api.GetGroupSessionsAsync(scenario.GroupReservationId, scenario.OwnerIdToken);

        // The endpoint always includes the Owner's own (so far empty) pre-order as a participant;
        // "no guests yet" means no *Guest*-type entries, not an empty list altogether.
        Assert.That(result.Participants, Has.All.Matches<ParticipantDto>(p => p.Type == "Owner"),
            "With nobody having joined yet, only the Owner should be listed.");
        Assert.That(result.Total, Is.EqualTo(0));
    }

    [Test(Description = "Guests who joined appear as participants, each with their own dishes/total, and the group total is the sum of all of them.")]
    public async Task GuestsWhoJoined_AppearWithTheirOwnDishesAndTotals()
    {
        await using var scenario = await GroupPreOrderScenario.CreateAsync();

        var (guest1Id, _, guest1Name) = await scenario.AddGuestAsync(addDish: true, quantity: 2, submit: true);
        var (guest2Id, _, guest2Name) = await scenario.AddGuestAsync(addDish: true, quantity: 1, submit: false);

        var result = await scenario.Api.GetGroupSessionsAsync(scenario.GroupReservationId, scenario.OwnerIdToken);

        // 2 guests + the Owner's own (empty) entry, which the endpoint always includes.
        Assert.That(result.Participants.FindAll(p => p.Type == "Guest"), Has.Count.EqualTo(2));

        var p1 = result.Participants.Find(p => p.Name == guest1Name);
        var p2 = result.Participants.Find(p => p.Name == guest2Name);

        Assert.That(p1, Is.Not.Null, "Guest who submitted their cart should be listed.");
        Assert.That(p1!.IsSubmitted, Is.True, "Guest 1 submitted their cart, so should be marked submitted.");
        Assert.That(p1.Total, Is.GreaterThan(0));

        Assert.That(p2, Is.Not.Null, "Guest who has not submitted yet should still be listed.");
        Assert.That(p2!.IsSubmitted, Is.False, "Guest 2 never submitted, so should be marked as not submitted.");

        Assert.That(result.Total, Is.EqualTo(p1.Total + p2.Total),
            "Group total should be the sum of every participant's total, submitted or not.");
    }

    [Test(Description = "A guest with no dishes yet (joined but added nothing) is still listed, with a total of 0.")]
    public async Task GuestWithNoDishes_IsStillListedWithZeroTotal()
    {
        await using var scenario = await GroupPreOrderScenario.CreateAsync();

        var (_, _, guestName) = await scenario.AddGuestAsync();

        var result = await scenario.Api.GetGroupSessionsAsync(scenario.GroupReservationId, scenario.OwnerIdToken);

        var participant = result.Participants.Find(p => p.Name == guestName);
        Assert.That(participant, Is.Not.Null);
        Assert.That(participant!.Total, Is.EqualTo(0));
        Assert.That(participant.IsSubmitted, Is.False);
    }

    [Test(Description = "Calling the endpoint with no Authorization header at all is rejected as unauthenticated.")]
    public async Task NoBearerToken_ReturnsUnauthorized()
    {
        await using var scenario = await GroupPreOrderScenario.CreateAsync();

        var response = await scenario.Api.GetGroupSessionsRawAsync(scenario.GroupReservationId, idToken: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test(Description = "A different, unrelated Customer's token is rejected - only the group's own Owner may view participants.")]
    public async Task TokenOfUnrelatedUser_ReturnsForbidden()
    {
        await using var scenario = await GroupPreOrderScenario.CreateAsync();
        var (_, otherUserToken) = await scenario.Api.RegisterAndSignInNewCustomerAsync("intruder");

        var response = await scenario.Api.GetGroupSessionsRawAsync(scenario.GroupReservationId, otherUserToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test(Description = "A well-formed but nonexistent group reservation id returns Not Found rather than an empty result.")]
    public async Task NonexistentGroupReservationId_ReturnsNotFound()
    {
        await using var ownerOnly = await GroupPreOrderScenario.CreateAsync();
        var madeUpGroupId = System.Guid.NewGuid().ToString();

        var response = await ownerOnly.Api.GetGroupSessionsRawAsync(madeUpGroupId, ownerOnly.OwnerIdToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
