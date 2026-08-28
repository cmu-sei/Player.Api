// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Concurrent;
using Player.Api.Data.Data.Models.Webhooks;
using Player.Api.Services;
using Player.Api.ViewModels.Webhooks;

namespace Player.Api.Tests.Support;

/// <summary>
/// Takes the place of the background webhook service and keeps the events handed to it, so that a test
/// can assert an entity event reached the queue without a delivery leaving the process.
/// </summary>
/// <remarks>
/// Written out for the same reason as <see cref="HubRecorder{THub}"/>: one instance serves the whole
/// run, and a substitute shared by every test loses calls under load. Filtering by predicate rather
/// than counting is what keeps a test honest here — every test in the suite raises events through this
/// one recorder, so a count is a fact about the suite and not about the request under test.
/// </remarks>
public sealed class WebhookRecorder : IBackgroundWebhookService
{
    private readonly ConcurrentQueue<WebhookEvent> _events = new();

    public Task AddEvent(WebhookEvent evt)
    {
        _events.Enqueue(evt);

        return Task.CompletedTask;
    }

    /// <summary>The events of a type whose payload matches, in the order they were raised.</summary>
    public IReadOnlyList<WebhookEvent> Recorded(EventType type, Func<string, bool> payload) =>
        [.. _events.Where(x => x.Type == type && payload(x.Payload ?? string.Empty))];
}
