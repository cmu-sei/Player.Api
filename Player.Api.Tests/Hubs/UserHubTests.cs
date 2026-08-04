// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Hubs;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Hubs;

/// <summary>
/// The per-user notification hub. Its group name is <c>view_user</c>, which is the whole of the isolation
/// between two users in the same view — the same user in two views is two separate groups.
/// </summary>
public class UserHubTests
{
    private readonly Guid _viewId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly HubHarness _harness = new();

    private string Group => $"{_viewId}_{_userId}";

    [Fact]
    public async Task Join_adds_the_caller_to_the_view_and_user_group_and_replies()
    {
        var joined = Message(_userId, "Successfully joined Test User notifications.");
        _notifications.JoinUser(_viewId, _userId, Arg.Any<CancellationToken>()).Returns(joined);

        await Hub().Join(_viewId.ToString(), _userId.ToString());

        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, Group, Arg.Any<CancellationToken>());
        Assert.Same(joined, HubHarness.Sent<Notification>(_harness.Caller, "Reply"));
    }

    /// <summary>
    /// Characterizes issue 33: joining someone else's notifications is refused through <c>WasSuccess</c>, which
    /// the hub does not read, so the connection is added to that user's group regardless.
    /// </summary>
    [Fact]
    public async Task A_refused_join_still_joins_the_group()
    {
        var refused = Message(_userId, "Failed to join Test User notifications.");
        refused.WasSuccess = false;
        _notifications.JoinUser(_viewId, _userId, Arg.Any<CancellationToken>()).Returns(refused);

        await Hub().Join(_viewId.ToString(), _userId.ToString());

        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, Group, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The hub compares against the user id, not the view — the real service always answers with the requested
    /// user, so this covers the branch rather than a reachable behaviour.
    /// </summary>
    [Fact]
    public async Task A_join_answered_for_another_user_does_not_join_the_group()
    {
        _notifications.JoinUser(_viewId, _userId, Arg.Any<CancellationToken>())
            .Returns(Message(Guid.NewGuid(), "Wrong user."));

        await Hub().Join(_viewId.ToString(), _userId.ToString());

        await _harness.Groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_goes_to_that_users_group_in_that_view()
    {
        var posted = Message(_userId, "Your VM is ready.");
        _notifications
            .PostToUser(_viewId, _userId,
                Arg.Is<Notification>(x => x.Text == "Your VM is ready." &&
                                          x.Subject == "User Notification"),
                Arg.Any<CancellationToken>())
            .Returns(posted);

        await Hub().Post(_viewId.ToString(), _userId.ToString(), "Your VM is ready.");

        Assert.Same(posted, HubHarness.Sent<Notification>(_harness.Group(Group), "Reply"));
        HubHarness.NothingSent(_harness.Caller, "Reply");
    }

    [Fact]
    public async Task A_post_answered_for_another_user_is_reported_only_to_the_caller()
    {
        _notifications.PostToUser(_viewId, _userId, Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Returns(Message(Guid.NewGuid(), "Your VM is ready."));

        await Hub().Post(_viewId.ToString(), _userId.ToString(), "Your VM is ready.");

        Assert.Equal("Message was not sent", HubHarness.Sent<Notification>(_harness.Caller, "Reply").Text);
        HubHarness.NothingSent(_harness.Group(Group), "Reply");
    }

    [Fact]
    public async Task GetHistory_returns_that_users_notifications_in_that_view()
    {
        var history = new[] { Message(_userId, "Earlier.") };
        _notifications.GetByUserAsync(_viewId, _userId, Arg.Any<CancellationToken>()).Returns(history);

        await Hub().GetHistory(_viewId.ToString(), _userId.ToString());

        Assert.Same(history, HubHarness.Sent<Notification[]>(_harness.Caller, "History"));
    }

    [Fact]
    public async Task Leave_removes_the_connection_from_the_group()
    {
        await Hub().Leave(_viewId.ToString(), _userId.ToString());

        await _harness.Groups.Received(1).RemoveFromGroupAsync(
            HubHarness.ConnectionId, Group, Arg.Any<CancellationToken>());
    }

    private UserHub Hub() => _harness.Attach(new UserHub(_notifications));

    private static Notification Message(Guid toId, string text) =>
        new() { ToId = toId, Text = text, WasSuccess = true };
}
