// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json;
using Crucible.Common.EntityEvents.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Player.Api.Data.Data.Models;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Events.EventHandlers.Webhooks;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Tests.Events.EventHandlers.Webhooks;

/// <summary>
/// The bridge from an entity event to a webhook delivery. The payload these handlers build is serialized into
/// the body subscribers receive, so its shape is a public contract; nothing downstream re-derives it from the
/// view.
/// </summary>
public class ViewHandlerTests
{
    private readonly IBackgroundWebhookService _sender = Substitute.For<IBackgroundWebhookService>();

    [Fact]
    public async Task A_created_view_queues_a_view_created_event()
    {
        var parent = Guid.NewGuid();
        var view = TestData.View("Exercise One");
        view.ParentViewId = parent;

        await new ViewCreatedHandler(NullLogger<ViewHandlerBase>.Instance, _sender)
            .Handle(new EntityCreated<ViewEntity>(view), TestContext.Current.CancellationToken);

        var queued = Queued();
        Assert.Equal(EventType.ViewCreated, queued.Type);

        var payload = Payload<ViewCreated>(queued);
        Assert.Equal(view.Id, payload.ViewId);
        Assert.Equal(parent, payload.ParentId);
        Assert.Equal("Exercise One", payload.ViewName);
    }

    /// <summary>
    /// Most views have no parent, and the payload distinguishes that from a parent of all zeros — a subscriber
    /// keying on <c>ParentId</c> would otherwise treat every top-level view as a child of the same view.
    /// </summary>
    [Fact]
    public async Task A_view_with_no_parent_queues_a_null_parent()
    {
        await new ViewCreatedHandler(NullLogger<ViewHandlerBase>.Instance, _sender)
            .Handle(new EntityCreated<ViewEntity>(TestData.View()), TestContext.Current.CancellationToken);

        Assert.Null(Payload<ViewCreated>(Queued()).ParentId);
    }

    /// <summary>
    /// A deletion carries the id alone: the row is already gone by the time subscribers are told, so anything
    /// else would have to be reconstructed from memory.
    /// </summary>
    [Fact]
    public async Task A_deleted_view_queues_a_view_deleted_event_with_only_the_id()
    {
        var view = TestData.View("Exercise One");

        await new ViewDeletedHandler(NullLogger<ViewHandlerBase>.Instance, _sender)
            .Handle(new EntityDeleted<ViewEntity>(view), TestContext.Current.CancellationToken);

        var queued = Queued();
        Assert.Equal(EventType.ViewDeleted, queued.Type);
        Assert.Equal(view.Id, Payload<ViewDeleted>(queued).ViewId);
        Assert.DoesNotContain("Exercise One", queued.Payload);
    }

    private WebhookEvent Queued()
    {
        var calls = _sender.ReceivedCalls().Where(x => x.GetMethodInfo().Name == nameof(_sender.AddEvent));

        return Assert.IsType<WebhookEvent>(Assert.Single(calls).GetArguments()[0]);
    }

    private static T Payload<T>(WebhookEvent queued) => JsonSerializer.Deserialize<T>(queued.Payload);
}
