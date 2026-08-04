// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Features.Webhooks;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Tests.Features.Webhooks;

/// <summary>
/// Covers the <c>Webhooks</c> feature's request handlers, including the subscription model's rule that a
/// client secret goes in but never comes back out.
/// </summary>
public class WebhookRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Create_persists_the_subscription_and_its_event_types()
    {
        var created = await SendAsync(new Create.Command
        {
            Name = "Listener",
            CallbackUri = "https://example.test/hook",
            ClientId = "client",
            ClientSecret = "secret",
            EventTypes = [EventType.ViewCreated, EventType.ViewDeleted]
        });

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
    [Fact]
    public async Task Create_does_not_return_the_client_secret()
    {
        var created = await SendAsync(new Create.Command
        {
            Name = "Listener",
            CallbackUri = "https://example.test/hook",
            ClientSecret = "secret",
            EventTypes = []
        });

        Assert.Empty(created.ClientSecret);
        Assert.True(created.ClientSecretSet);
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageWebhookSubscriptions()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Create.Command { Name = "Nope", EventTypes = [] }));
    }

    [Fact]
    public async Task GetAll_returns_every_subscription()
    {
        await Seed(
            TestData.Webhook("First", eventTypes: [EventType.ViewCreated]),
            TestData.Webhook("Second"));

        var subscriptions = await SendAsync(new GetAll.Query());

        Assert.Equal(2, subscriptions.Length);
        Assert.Equal(
            [EventType.ViewCreated],
            subscriptions.Single(x => x.Name == "First").EventTypes);
    }

    [Fact]
    public async Task GetAll_is_forbidden_without_ViewWebhookSubscriptions()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new GetAll.Query()));
    }

    [Fact]
    public async Task Edit_replaces_the_event_types()
    {
        var webhook = TestData.Webhook(eventTypes: [EventType.ViewCreated]);
        await Seed(webhook);

        var edited = await SendAsync(new Edit.Command
        {
            Id = webhook.Id,
            Form = new WebhookSubscriptionForm
            {
                Name = "Renamed",
                CallbackUri = "https://example.test/other",
                EventTypes = [EventType.ViewDeleted]
            }
        });

        Assert.Equal("Renamed", edited.Name);
        Assert.Equal([EventType.ViewDeleted], edited.EventTypes);
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

        var edited = await SendAsync(new Edit.Command
        {
            Id = webhook.Id,
            Form = new WebhookSubscriptionPartialEditForm { CallbackUri = "https://example.test/moved" }
        });

        Assert.Equal("Original", edited.Name);
        Assert.Equal("https://example.test/moved", edited.CallbackUri);
        Assert.Equal([EventType.ViewCreated], edited.EventTypes);
    }

    [Fact]
    public async Task Edit_reports_a_missing_subscription_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<WebhookSubscription>>(
            () => SendAsync(new Edit.Command
            {
                Id = Guid.NewGuid(),
                Form = new WebhookSubscriptionPartialEditForm { Name = "Ghost" }
            }));
    }

    [Fact]
    public async Task Edit_is_forbidden_without_ManageWebhookSubscriptions()
    {
        var webhook = TestData.Webhook();
        await Seed(webhook);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Edit.Command
            {
                Id = webhook.Id,
                Form = new WebhookSubscriptionPartialEditForm { Name = "Nope" }
            }));
    }

    [Fact]
    public async Task Delete_removes_the_subscription()
    {
        var webhook = TestData.Webhook();
        await Seed(webhook);

        await SendAsync(new Delete.Command { Id = webhook.Id });

        await using var db = NewContext();
        Assert.False(await db.Webhooks.AnyAsync(Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageWebhookSubscriptions()
    {
        var webhook = TestData.Webhook();
        await Seed(webhook);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new Delete.Command { Id = webhook.Id }));
    }
}
