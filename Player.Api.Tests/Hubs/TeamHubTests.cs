// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Hubs;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Player.Api.ViewModels;

namespace Player.Api.Tests.Hubs;

/// <summary>
/// The team notification hub. Group membership is what decides who receives a later broadcast, so which
/// connections <c>Join</c> adds to the group is the security-relevant part of this class.
/// </summary>
public class TeamHubTests
{
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly HubHarness _harness = new();

    [Fact]
    public async Task Join_adds_the_caller_to_the_team_group_and_replies()
    {
        var joined = Message(_teamId, "Successfully joined Red Team notifications.");
        _notifications.JoinTeam(_teamId, Arg.Any<CancellationToken>()).Returns(joined);

        await Hub().Join(_teamId.ToString());

        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, _teamId.ToString(), Arg.Any<CancellationToken>());
        Assert.Same(joined, HubHarness.Sent<Notification>(_harness.Caller, "Reply"));
    }

    /// <summary>
    /// Characterizes issue 33: a refused join is reported by <c>WasSuccess</c>, which the hub never reads, so
    /// the connection joins the group and receives every later broadcast anyway. The hub's own check is on
    /// <c>ToId</c>, which the service sets to the requested id whether or not the caller was authorized.
    /// </summary>
    [Fact]
    public async Task A_refused_join_still_joins_the_group()
    {
        var refused = Message(_teamId, "Failed to join Red Team notifications.");
        refused.WasSuccess = false;
        _notifications.JoinTeam(_teamId, Arg.Any<CancellationToken>()).Returns(refused);

        await Hub().Join(_teamId.ToString());

        await _harness.Groups.Received(1).AddToGroupAsync(
            HubHarness.ConnectionId, _teamId.ToString(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The only case the hub's <c>ToId</c> check rejects. The real service cannot produce it — it always
    /// answers with the requested id — so this covers the branch rather than a reachable behaviour.
    /// </summary>
    [Fact]
    public async Task A_join_answered_for_another_team_does_not_join_the_group()
    {
        _notifications.JoinTeam(_teamId, Arg.Any<CancellationToken>())
            .Returns(Message(Guid.NewGuid(), "Wrong team."));

        await Hub().Join(_teamId.ToString());

        await _harness.Groups.DidNotReceive().AddToGroupAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.NotNull(HubHarness.Sent<Notification>(_harness.Caller, "Reply"));
    }

    /// <summary>
    /// A client sends text only; the hub supplies the subject. What comes back goes to the group rather than
    /// the caller, so the caller sees its own message by virtue of being in the group.
    /// </summary>
    [Fact]
    public async Task Post_broadcasts_to_the_team_group()
    {
        var posted = Message(_teamId, "Ten minutes remaining.");
        _notifications
            .PostToTeam(_teamId, Arg.Is<Notification>(x => x.Text == "Ten minutes remaining." &&
                                                           x.Subject == "Team Notification"),
                Arg.Any<CancellationToken>())
            .Returns(posted);

        await Hub().Post(_teamId.ToString(), "Ten minutes remaining.");

        Assert.Same(posted, HubHarness.Sent<Notification>(_harness.Group(_teamId.ToString()), "Reply"));
        HubHarness.NothingSent(_harness.Caller, "Reply");
    }

    [Fact]
    public async Task A_post_answered_for_another_team_is_reported_only_to_the_caller()
    {
        _notifications.PostToTeam(_teamId, Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Returns(Message(Guid.NewGuid(), "Ten minutes remaining."));

        await Hub().Post(_teamId.ToString(), "Ten minutes remaining.");

        Assert.Equal("Message was not sent", HubHarness.Sent<Notification>(_harness.Caller, "Reply").Text);
        HubHarness.NothingSent(_harness.Group(_teamId.ToString()), "Reply");
    }

    [Fact]
    public async Task GetHistory_returns_the_teams_notifications_to_the_caller_alone()
    {
        var history = new[] { Message(_teamId, "Earlier.") };
        _notifications.GetByTeamAsync(_teamId, Arg.Any<CancellationToken>()).Returns(history);

        await Hub().GetHistory(_teamId.ToString());

        Assert.Same(history, HubHarness.Sent<Notification[]>(_harness.Caller, "History"));
    }

    [Fact]
    public async Task Leave_removes_the_connection_from_the_group()
    {
        await Hub().Leave(_teamId.ToString());

        await _harness.Groups.Received(1).RemoveFromGroupAsync(
            HubHarness.ConnectionId, _teamId.ToString(), Arg.Any<CancellationToken>());
    }

    /// <summary>Every method takes the team id as a string, so an unparseable one is the caller's error.</summary>
    [Fact]
    public async Task A_malformed_team_id_is_rejected()
    {
        await Assert.ThrowsAsync<FormatException>(() => Hub().Join("not-a-guid"));
    }

    private TeamHub Hub() =>
        _harness.Attach(new TeamHub(Substitute.For<ITeamService>(), _notifications));

    private static Notification Message(Guid toId, string text) =>
        new() { ToId = toId, Text = text, WasSuccess = true };
}
