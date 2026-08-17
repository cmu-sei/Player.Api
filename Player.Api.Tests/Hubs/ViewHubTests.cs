// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using Player.Api.Hubs;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Hubs;

/// <summary>
/// The view notification hub, which also carries presence: joining records the connection so other members can
/// see the user online, and every exit path has to undo that or the user is shown online forever.
/// </summary>
public class ViewHubTests
{
    private readonly Guid _viewId = Guid.NewGuid();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IPresenceService _presence = Substitute.For<IPresenceService>();
    private readonly IXApiService _xApi = Substitute.For<IXApiService>();
    private readonly HubHarness _harness = new();

    [Fact]
    public async Task Join_adds_the_caller_to_the_view_group_and_replies()
    {
        var joined = Message(_viewId, "Successfully joined Exercise One notifications.");
        _notifications.JoinView(_viewId, Arg.Any<CancellationToken>()).Returns(joined);

        await Hub().Join(_viewId.ToString());

        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, _viewId.ToString(), Arg.Any<CancellationToken>());
        Assert.Same(joined, HubHarness.Sent<Notification>(_harness.Caller, "Reply"));
    }

    /// <summary>
    /// Characterizes issue 33: a refused join is reported by <c>WasSuccess</c>, which the hub never reads, so
    /// the connection joins the group and receives every later view broadcast anyway.
    /// </summary>
    [Fact]
    public async Task A_refused_join_still_joins_the_group()
    {
        var refused = Message(_viewId, "Failed to join Exercise One notifications.");
        refused.WasSuccess = false;
        _notifications.JoinView(_viewId, Arg.Any<CancellationToken>()).Returns(refused);

        await Hub().Join(_viewId.ToString());

        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, _viewId.ToString(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The presence id is kept on the connection, because <c>Leave</c> and <c>OnDisconnectedAsync</c> need it
    /// to release the presence and have nothing else to look it up by.
    /// </summary>
    [Fact]
    public async Task Join_registers_presence_and_remembers_it_on_the_connection()
    {
        var presenceId = Guid.NewGuid();
        JoinReturns();
        _presence.AddConnectionToView(_viewId, _harness.UserId, HubHarness.ConnectionId,
            Arg.Any<CancellationToken>()).Returns(presenceId);

        await Hub().Join(_viewId.ToString());

        Assert.Equal(presenceId, _harness.Items["presenceId"]);
    }

    [Fact]
    public async Task Join_emits_the_xapi_view_viewed_statement()
    {
        JoinReturns();

        await Hub().Join(_viewId.ToString());

        await _xApi.Received(1).EmitViewViewedAsync(_viewId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Presence and xAPI are secondary to being in the group: if either throws the join still completes and the
    /// caller still gets its reply, so a failing presence store cannot keep users out of a view.
    /// </summary>
    [Fact]
    public async Task A_join_completes_even_when_presence_and_xapi_fail()
    {
        var joined = Message(_viewId, "Successfully joined Exercise One notifications.");
        _notifications.JoinView(_viewId, Arg.Any<CancellationToken>()).Returns(joined);
        _presence.AddConnectionToView(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("presence is down"));
        _xApi.EmitViewViewedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the lrs is down"));

        await Hub().Join(_viewId.ToString());

        Assert.Same(joined, HubHarness.Sent<Notification>(_harness.Caller, "Reply"));
        Assert.False(_harness.Items.ContainsKey("presenceId"));
    }

    [Fact]
    public async Task Post_broadcasts_to_the_view_group()
    {
        var posted = Message(_viewId, "Ten minutes remaining.");
        _notifications
            .PostToView(_viewId,
                Arg.Is<Notification>(x => x.Text == "Ten minutes remaining." &&
                                          x.Subject == "View Notification"),
                Arg.Any<CancellationToken>())
            .Returns(posted);

        await Hub().Post(_viewId.ToString(), "Ten minutes remaining.");

        Assert.Same(posted, HubHarness.Sent<Notification>(_harness.Group(_viewId.ToString()), "Reply"));
        HubHarness.NothingSent(_harness.Caller, "Reply");
    }

    [Fact]
    public async Task A_post_answered_for_another_view_is_reported_only_to_the_caller()
    {
        _notifications.PostToView(_viewId, Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Returns(Message(Guid.NewGuid(), "Ten minutes remaining."));

        await Hub().Post(_viewId.ToString(), "Ten minutes remaining.");

        Assert.Equal("Message was not sent", HubHarness.Sent<Notification>(_harness.Caller, "Reply").Text);
        HubHarness.NothingSent(_harness.Group(_viewId.ToString()), "Reply");
    }

    [Fact]
    public async Task GetHistory_returns_the_views_notifications_to_the_caller_alone()
    {
        var history = new[] { Message(_viewId, "Earlier.") };
        _notifications.GetByViewAsync(_viewId, Arg.Any<CancellationToken>()).Returns(history);

        await Hub().GetHistory(_viewId.ToString());

        Assert.Same(history, HubHarness.Sent<Notification[]>(_harness.Caller, "History"));
    }

    [Fact]
    public async Task Leave_releases_the_presence_and_leaves_the_group()
    {
        var presenceId = Guid.NewGuid();
        _harness.Items["presenceId"] = presenceId;

        await Hub().Leave(_viewId.ToString());

        await _presence.Received(1).RemoveConnectionFromView(
            presenceId, _harness.UserId, HubHarness.ConnectionId, Arg.Any<CancellationToken>());
        await _harness.Groups.Received(1).RemoveFromGroupAsync(
            HubHarness.ConnectionId, _viewId.ToString(), Arg.Any<CancellationToken>());
        Assert.False(_harness.Items.ContainsKey("presenceId"));
    }

    /// <summary>
    /// A connection that never joined — or that has already left — has no presence to release, and leaving the
    /// group is still attempted, which SignalR treats as a no-op.
    /// </summary>
    [Fact]
    public async Task Leave_without_a_presence_only_leaves_the_group()
    {
        await Hub().Leave(_viewId.ToString());

        await _presence.DidNotReceive().RemoveConnectionFromView(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _harness.Groups.Received(1).RemoveFromGroupAsync(
            HubHarness.ConnectionId, _viewId.ToString(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A dropped connection never calls <c>Leave</c>, so the disconnect path has to release the presence too —
    /// otherwise a closed browser tab leaves the user shown online indefinitely.
    /// </summary>
    [Fact]
    public async Task A_dropped_connection_releases_the_presence()
    {
        var presenceId = Guid.NewGuid();
        _harness.Items["presenceId"] = presenceId;

        await Hub().OnDisconnectedAsync(new IOException("connection reset"));

        await _presence.Received(1).RemoveConnectionFromView(
            presenceId, _harness.UserId, HubHarness.ConnectionId, Arg.Any<CancellationToken>());
        Assert.False(_harness.Items.ContainsKey("presenceId"));
    }

    [Fact]
    public async Task A_dropped_connection_that_never_joined_releases_no_presence()
    {
        await Hub().OnDisconnectedAsync(null);

        await _presence.DidNotReceive().RemoveConnectionFromView(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Presence updates are pushed per team group, so a caller has to join one group per team it may see. The
    /// service decides which those are; the hub only joins what it is given.
    /// </summary>
    [Fact]
    public async Task JoinPresence_joins_every_group_the_caller_may_see_and_returns_the_current_presence()
    {
        var presence = new[] { new ViewPresence() };
        _presence.GetGroupsByViewId(_viewId).Returns(["group-a", "group-b"]);
        _presence.GetPresenceByViewId(_viewId).Returns(presence);

        var result = await Hub().JoinPresence(_viewId);

        Assert.Same(presence, result);
        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, "group-a", Arg.Any<CancellationToken>());
        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, "group-b", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Narrowing to one team is a different overload on the service, not a filter over the view's groups — a
    /// caller watching one team must not be subscribed to the rest.
    /// </summary>
    [Fact]
    public async Task JoinPresenceForTeam_joins_only_that_teams_groups()
    {
        var teamId = Guid.NewGuid();
        var presence = new[] { new ViewPresence() };
        _presence.GetGroupsByViewId(_viewId, teamId).Returns(["team-group"]);
        _presence.GetPresenceByViewId(_viewId, teamId).Returns(presence);

        var result = await Hub().JoinPresenceForTeam(_viewId, teamId);

        Assert.Same(presence, result);
        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, "team-group", Arg.Any<CancellationToken>());
        await _presence.DidNotReceive().GetGroupsByViewId(_viewId);
    }

    [Fact]
    public async Task LeavePresence_leaves_every_group_of_the_view()
    {
        _presence.GetGroupsByViewId(_viewId).Returns(["group-a", "group-b"]);

        await Hub().LeavePresence(_viewId);

        await _harness.Groups.Received(1).RemoveFromGroupAsync(
            HubHarness.ConnectionId, "group-a", Arg.Any<CancellationToken>());
        await _harness.Groups.Received(1).RemoveFromGroupAsync(
            HubHarness.ConnectionId, "group-b", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The group has to be named, not merely counted: a call count alone holds just as well if the hub
    /// joined the group instead of leaving it, or left a group it was not asked about — and either would
    /// leave a caller subscribed to a team's presence after it left.
    /// </summary>
    [Fact]
    public async Task LeavePresenceForTeam_leaves_only_that_teams_groups()
    {
        var teamId = Guid.NewGuid();
        _presence.GetGroupsByViewId(_viewId, teamId).Returns(["team-group"]);

        await Hub().LeavePresenceForTeam(_viewId, teamId);

        await _harness.Groups.Received(1).RemoveFromGroupAsync(
            HubHarness.ConnectionId, "team-group", Arg.Any<CancellationToken>());
        // And nothing beyond it, which is the "only" in the name.
        Assert.Single(_harness.Groups.ReceivedCalls());
        await _presence.DidNotReceive().GetGroupsByViewId(_viewId);
    }

    private void JoinReturns() =>
        _notifications.JoinView(_viewId, Arg.Any<CancellationToken>())
            .Returns(Message(_viewId, "Successfully joined Exercise One notifications."));

    private ViewHub Hub() =>
        _harness.Attach(new ViewHub(_notifications, _presence, _xApi, NullLogger<ViewHub>.Instance));

    private static Notification Message(Guid toId, string text) =>
        new() { ToId = toId, Text = text, WasSuccess = true };
}
