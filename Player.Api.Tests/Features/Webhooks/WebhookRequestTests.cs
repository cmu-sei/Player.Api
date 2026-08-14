// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Tests.Support;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Tests.Features.Webhooks;

/// <summary>
/// Covers the <c>Webhooks</c> feature over HTTP, including the subscription model's rule that a client
/// secret goes in but never comes back out.
/// </summary>
public class WebhookRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    /// <summary>Create is the one route in this feature that is not <c>api/webhooks</c>.</summary>
    private const string CreateRoute = "api/webhooks/subscribe";

    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_subscription_and_its_event_types()
    {
        var created = await ReadAsync<WebhookSubscription>(await RootClient.PostAsJsonAsync(
            CreateRoute,
            new
            {
                name = "Listener",
                callbackUri = "https://example.test/hook",
                clientId = "client",
                clientSecret = "secret",
                eventTypes = new[] { EventType.ViewCreated, EventType.ViewDeleted }
            },
            Ct));

        Assert.Equal("Listener", created.Name);
        Assert.Equal([EventType.ViewCreated, EventType.ViewDeleted], created.EventTypes);

        await using var db = NewContext();
        var entity = await db.Webhooks.Include(x => x.EventTypes).SingleAsync(Ct);
        Assert.Equal("secret", entity.ClientSecret);
        Assert.Equal(2, entity.EventTypes.Count);
    }

    /// <summary>
    /// The response reports only that a secret is set. Returning the value would hand it back to every
    /// caller allowed to list subscriptions.
    /// </summary>
    /// <remarks>
    /// Asserted on the wire rather than on a deserialized <see cref="WebhookSubscription"/>: the view
    /// model's getter answers <c>string.Empty</c> for a secret that is set and its setter stores what it
    /// is handed, so reading the body back into the type turns "set" into "unset".
    /// </remarks>
    [Fact]
    public async Task Create_does_not_return_the_client_secret()
    {
        var response = await RootClient.PostAsJsonAsync(
            CreateRoute,
            new
            {
                name = "Listener",
                callbackUri = "https://example.test/hook",
                clientSecret = "not-in-the-body",
                eventTypes = Array.Empty<EventType>()
            },
            Ct);

        await AssertStatus(HttpStatusCode.OK, response);

        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("not-in-the-body", body);

        using var created = JsonDocument.Parse(body);
        Assert.Empty(created.RootElement.GetProperty("clientSecret").GetString());
        Assert.True(created.RootElement.GetProperty("clientSecretSet").GetBoolean());
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageWebhookSubscriptions()
    {
        var actor = await Actor()
            .WithSystemPermissions(SystemPermission.ViewWebhookSubscriptions)
            .SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync(
            CreateRoute,
            new { name = "Nope", eventTypes = Array.Empty<EventType>() },
            Ct));
    }

    // ---- GetAll ---------------------------------------------------------------------------------

    [Fact]
    public async Task GetAll_returns_every_subscription()
    {
        await Seed(
            TestData.Webhook("First", eventTypes: [EventType.ViewCreated]),
            TestData.Webhook("Second"));

        var subscriptions = await ReadAsync<WebhookSubscription[]>(
            await RootClient.GetAsync("api/webhooks", Ct));

        Assert.Equal(2, subscriptions.Length);
        Assert.Equal(
            [EventType.ViewCreated],
            subscriptions.Single(x => x.Name == "First").EventTypes);
    }

    /// <summary>
    /// The two webhook permissions are separate, so the one that manages subscriptions does not also
    /// list them.
    /// </summary>
    [Fact]
    public async Task GetAll_is_forbidden_without_ViewWebhookSubscriptions()
    {
        var actor = await Actor()
            .WithSystemPermissions(SystemPermission.ManageWebhookSubscriptions)
            .SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync("api/webhooks", Ct));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>PUT</c> takes the full form, whose mapping replaces the event-type rows rather than adding to
    /// them.
    /// </summary>
    [Fact]
    public async Task Edit_replaces_the_event_types()
    {
        var webhook = TestData.Webhook(eventTypes: [EventType.ViewCreated]);
        await Seed(webhook);

        var edited = await ReadAsync<WebhookSubscription>(await RootClient.PutAsJsonAsync(
            $"api/webhooks/{webhook.Id}",
            new
            {
                name = "Renamed",
                callbackUri = "https://example.test/other",
                eventTypes = new[] { EventType.ViewDeleted }
            },
            Ct));

        Assert.Equal("Renamed", edited.Name);
        Assert.Equal([EventType.ViewDeleted], edited.EventTypes);

        await using var db = NewContext();
        var stored = await db.Webhooks.Include(x => x.EventTypes).SingleAsync(Ct);
        Assert.Equal(EventType.ViewDeleted, Assert.Single(stored.EventTypes).EventType);
    }

    /// <summary>
    /// The other half of the secret rule, and its cost: <c>PUT</c> maps every member unconditionally, so
    /// a client that read a subscription back and sent it again clears a secret it was never shown.
    /// </summary>
    /// <remarks>
    /// Turns red when the full form stops overwriting a stored secret with an absent one — <c>PATCH</c>
    /// is the route that already leaves it alone.
    /// </remarks>
    [Fact]
    public async Task Edit_with_the_full_form_clears_the_client_secret_it_never_returned()
    {
        var webhook = TestData.Webhook();
        await Seed(webhook);

        await ReadAsync<WebhookSubscription>(await RootClient.PutAsJsonAsync(
            $"api/webhooks/{webhook.Id}",
            new
            {
                name = webhook.Name,
                callbackUri = webhook.CallbackUri,
                eventTypes = Array.Empty<EventType>()
            },
            Ct));

        await using var db = NewContext();
        Assert.Null((await db.Webhooks.SingleAsync(Ct)).ClientSecret);
    }

    /// <summary>
    /// The partial form's mapping skips null members, so an omitted field keeps its stored value rather
    /// than being cleared.
    /// </summary>
    [Fact]
    public async Task Edit_leaves_omitted_fields_alone_for_a_partial_form()
    {
        var webhook = TestData.Webhook("Original", eventTypes: [EventType.ViewCreated]);
        await Seed(webhook);

        var edited = await ReadAsync<WebhookSubscription>(await RootClient.PatchAsJsonAsync(
            $"api/webhooks/{webhook.Id}",
            new { callbackUri = "https://example.test/moved" },
            Ct));

        Assert.Equal("Original", edited.Name);
        Assert.Equal("https://example.test/moved", edited.CallbackUri);
        Assert.Equal([EventType.ViewCreated], edited.EventTypes);

        // Read from the database because the secret is the one omitted field no response reveals.
        await using var db = NewContext();
        Assert.Equal("test-secret", (await db.Webhooks.SingleAsync(Ct)).ClientSecret);
    }

    [Fact]
    public async Task Edit_reports_a_missing_subscription_as_not_found()
    {
        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PatchAsJsonAsync(
            $"api/webhooks/{Guid.NewGuid()}", new { name = "Ghost" }, Ct));
    }

    [Fact]
    public async Task Edit_is_forbidden_without_ManageWebhookSubscriptions()
    {
        var webhook = TestData.Webhook();
        await Seed(webhook);

        var actor = await Actor()
            .WithSystemPermissions(SystemPermission.ViewWebhookSubscriptions)
            .SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).PatchAsJsonAsync(
            $"api/webhooks/{webhook.Id}", new { name = "Nope" }, Ct));
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_subscription()
    {
        var webhook = TestData.Webhook();
        await Seed(webhook);

        await AssertStatus(
            HttpStatusCode.NoContent,
            await RootClient.DeleteAsync($"api/webhooks/{webhook.Id}", Ct));

        await using var db = NewContext();
        Assert.False(await db.Webhooks.AnyAsync(Ct));
    }

    /// <summary>
    /// Wrong: the delete handler resolves the row with <c>SingleAsync</c> where edit uses
    /// <c>SingleOrDefaultAsync</c>, so the same missing id is a 404 from one route and a 500 from the
    /// other.
    /// </summary>
    /// <remarks>
    /// Turns red when <c>Delete.cs:58</c> throws <c>EntityNotFoundException</c> for a row that is gone.
    /// </remarks>
    [Fact]
    public async Task Delete_reports_a_missing_subscription_as_a_server_error()
    {
        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.DeleteAsync($"api/webhooks/{Guid.NewGuid()}", Ct));

        Assert.Equal("Sequence contains no elements.", problem.Detail);
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageWebhookSubscriptions()
    {
        var webhook = TestData.Webhook();
        await Seed(webhook);

        var actor = await Actor()
            .WithSystemPermissions(SystemPermission.ViewWebhookSubscriptions)
            .SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).DeleteAsync($"api/webhooks/{webhook.Id}", Ct));

        await using var db = NewContext();
        Assert.True(await db.Webhooks.AnyAsync(Ct));
    }
}
