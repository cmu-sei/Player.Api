// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Notification = Player.Api.ViewModels.Notification;
using User = Player.Api.Features.Users.User;

namespace Player.Api.Tests.Services;

/// <summary>
/// The chat and system-message service behind the SignalR hubs. Every join returns a notification
/// describing whether it worked, so the interesting behaviour is in what that reply says rather than in
/// what it throws.
/// </summary>
public class NotificationServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    // ---- Reads ------------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_returns_every_notification()
    {
        var view = TestData.View();
        await Seed(
            view,
            TestData.Notification(view.Id, text: "For this view"),
            TestData.Notification(Guid.NewGuid(), text: "For another view"));

        // "Every" means both recipients, including the one whose view does not exist, so the two are named
        // rather than counted.
        Assert.Equal(
            ["For another view", "For this view"],
            (await Service().GetAsync(Ct)).Select(x => x.Text).Order());
    }

    /// <summary>
    /// Stored timestamps come back without a kind, so they are stamped UTC on the way out — otherwise a
    /// client would read them as local time.
    /// </summary>
    [Fact]
    public async Task GetAsync_returns_broadcast_times_as_UTC()
    {
        var view = TestData.View();
        await Seed(view, TestData.Notification(view.Id, broadcastTime: new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc)));

        var notification = Assert.Single(await Service().GetAsync(Ct));
        Assert.Equal(DateTimeKind.Utc, notification.BroadcastTime.Kind);
    }

    /// <summary>The near miss is ManageViews where listing every notification takes ViewViews.</summary>
    [Fact]
    public async Task GetAsync_is_forbidden_for_a_caller_holding_only_ManageViews()
    {
        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageViews).Build();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(caller).GetAsync(Ct));
    }

    [Fact]
    public async Task GetAsync_is_forbidden_for_a_view_member_without_ViewViews()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(Member(view.Id, team.Id, ViewPermission.ViewView)).GetAsync(Ct));
    }

    /// <summary>
    /// The view's own conversation: only view-addressed messages, and not the system ones a join emits.
    /// </summary>
    [Fact]
    public async Task GetByViewAsync_returns_the_views_non_system_notifications()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(
            view,
            team,
            TestData.Notification(view.Id, text: "For the view"),
            TestData.Notification(view.Id, priority: NotificationPriority.System, text: "Joined"),
            TestData.Notification(team.Id, NotificationType.Team, text: "For the team"),
            TestData.Notification(Guid.NewGuid(), text: "For another view"));

        var notifications = await Service(Member(view.Id, team.Id)).GetByViewAsync(view.Id, Ct);

        Assert.Equal("For the view", Assert.Single(notifications).Text);
    }

    [Fact]
    public async Task GetByViewAsync_returns_the_newest_notification_first()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(
            view,
            team,
            TestData.Notification(view.Id, text: "Older", broadcastTime: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            TestData.Notification(view.Id, text: "Newer", broadcastTime: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));

        var notifications = await Service(Member(view.Id, team.Id)).GetByViewAsync(view.Id, Ct);

        Assert.Equal(["Newer", "Older"], notifications.Select(x => x.Text));
    }

    /// <summary>
    /// Membership is the test, not permission: this is the one read that a system administrator who
    /// belongs to no team cannot make.
    /// </summary>
    [Fact]
    public async Task GetByViewAsync_is_forbidden_for_a_caller_who_is_not_in_the_view()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service().GetByViewAsync(view.Id, Ct));
    }

    /// <summary>
    /// The near miss is a member of another view holding ViewView there: the check is membership of this
    /// view, and a claim on another view does not count.
    /// </summary>
    [Fact]
    public async Task GetByViewAsync_is_forbidden_for_a_caller_holding_ViewView_only_in_another_view()
    {
        var view = TestData.View();
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id);
        await Seed(view, other, otherTeam, TestData.Notification(view.Id, text: "For the view"));

        var caller = Member(other.Id, otherTeam.Id, ViewPermission.ViewView);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(caller).GetByViewAsync(view.Id, Ct));
    }

    /// <summary>
    /// Unlike the others this one authorizes nothing — the request handler does it, and the sweep behind
    /// a view delete needs every message including the system ones.
    /// </summary>
    [Fact]
    public async Task GetAllViewNotificationsAsync_returns_everything_addressed_to_or_within_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var otherView = TestData.View();
        var otherTeam = TestData.Team(otherView.Id);
        var user = TestData.User();
        await Seed(
            view,
            team,
            otherView,
            otherTeam,
            user,
            TestData.Notification(view.Id, priority: NotificationPriority.System, text: "Joined"),
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "To a member"),
            TestData.Notification(team.Id, NotificationType.Team, text: "To a team"),
            TestData.Notification(otherTeam.Id, NotificationType.Team, text: "To another view's team"),
            TestData.Notification(Guid.NewGuid(), text: "Elsewhere"));

        var notifications = await Service(ClaimsPrincipalBuilder.Anonymous())
            .GetAllViewNotificationsAsync(view.Id, Ct);

        Assert.Equal(["Joined", "To a member", "To a team"], notifications.Select(x => x.Text).Order());
    }

    /// <summary>
    /// A match on the view id as the recipient counts only for a notification addressed to a view, so a
    /// team or user notification whose recipient id happens to equal the view's id is not listed.
    /// </summary>
    [Theory]
    [InlineData(NotificationType.Team)]
    [InlineData(NotificationType.User)]
    public async Task GetAllViewNotificationsAsync_leaves_out_a_notification_to_another_type_whose_recipient_id_is_the_view_id(
        NotificationType toType)
    {
        var view = TestData.View();
        await Seed(
            view,
            TestData.Notification(view.Id, text: "To the view"),
            TestData.Notification(view.Id, toType, text: "Lookalike"));

        var notifications = await Service(ClaimsPrincipalBuilder.Anonymous())
            .GetAllViewNotificationsAsync(view.Id, Ct);

        Assert.Equal("To the view", Assert.Single(notifications).Text);
    }

    [Fact]
    public async Task GetByTeamAsync_returns_the_teams_non_system_notifications()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(
            view,
            team,
            TestData.Notification(team.Id, NotificationType.Team, text: "For the team"),
            TestData.Notification(team.Id, NotificationType.Team, priority: NotificationPriority.System),
            TestData.Notification(view.Id, text: "For the view"));

        var notifications = await Service().GetByTeamAsync(team.Id, Ct);

        Assert.Equal("For the team", Assert.Single(notifications).Text);
    }

    [Fact]
    public async Task GetByTeamAsync_is_forbidden_for_a_caller_holding_ViewTeam_only_on_another_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other Team");
        await Seed(view, team, other);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, other.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).GetByTeamAsync(team.Id, Ct));
    }

    [Fact]
    public async Task GetByUserAsync_returns_the_users_notifications_to_an_observer_of_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(
            view,
            team,
            user,
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "For the user"),
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, priority: NotificationPriority.System));

        var notifications = await Service(Member(view.Id, team.Id, ViewPermission.ViewView)).GetByUserAsync(view.Id, user.Id, Ct);

        Assert.Equal("For the user", Assert.Single(notifications).Text);
    }

    [Fact]
    public async Task GetByUserAsync_returns_the_users_notifications_to_a_manager_of_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(
            view,
            team,
            user,
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "For the user"));

        var notifications = await Service(Member(view.Id, team.Id, ViewPermission.ManageView)).GetByUserAsync(view.Id, user.Id, Ct);

        Assert.Equal("For the user", Assert.Single(notifications).Text);
    }

    /// <summary>
    /// Membership of the view lets a caller read their own conversation, not anyone else's.
    /// </summary>
    [Fact]
    public async Task GetByUserAsync_is_forbidden_for_another_users_notifications_to_a_plain_member()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(
            view,
            team,
            user,
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "Not yours"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(Member(view.Id, team.Id)).GetByUserAsync(view.Id, user.Id, Ct));
    }

    /// <summary>
    /// The history is the conversation within one view, so another view's messages to the same user stay out.
    /// </summary>
    [Fact]
    public async Task GetByUserAsync_returns_only_the_notifications_of_the_requested_view()
    {
        var view = TestData.View();
        var otherView = TestData.View();
        var caller = new ClaimsPrincipalBuilder().Build();
        await Seed(
            view,
            otherView,
            TestData.Notification(caller.GetId(), NotificationType.User, viewId: view.Id, text: "This view"),
            TestData.Notification(caller.GetId(), NotificationType.User, viewId: otherView.Id, text: "Other view"));

        var notifications = await Service(caller).GetByUserAsync(view.Id, caller.GetId(), Ct);

        Assert.Equal("This view", Assert.Single(notifications).Text);
    }

    /// <summary>
    /// A user can read their own messages in a view they are not a member of, which is how a message
    /// outlives the membership it was sent to.
    /// </summary>
    [Fact]
    public async Task GetByUserAsync_allows_a_caller_to_read_their_own_notifications()
    {
        var view = TestData.View();
        var caller = new ClaimsPrincipalBuilder().Build();
        await Seed(
            view,
            TestData.Notification(caller.GetId(), NotificationType.User, viewId: view.Id, text: "Mine"));

        var notifications = await Service(caller).GetByUserAsync(view.Id, caller.GetId(), Ct);

        Assert.Equal("Mine", Assert.Single(notifications).Text);
    }

    /// <summary>The system tier: ViewViews observes every view, so it reads anyone's conversation without membership.</summary>
    [Fact]
    public async Task GetByUserAsync_returns_the_users_notifications_to_a_caller_holding_only_ViewViews()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(
            view,
            user,
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "For the user"));

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewViews).Build();
        var notifications = await Service(caller).GetByUserAsync(view.Id, user.Id, Ct);

        Assert.Equal("For the user", Assert.Single(notifications).Text);
    }

    /// <summary>The same system tier through ManageViews.</summary>
    [Fact]
    public async Task GetByUserAsync_returns_the_users_notifications_to_a_caller_holding_only_ManageViews()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(
            view,
            user,
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "For the user"));

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageViews).Build();
        var notifications = await Service(caller).GetByUserAsync(view.Id, user.Id, Ct);

        Assert.Equal("For the user", Assert.Single(notifications).Text);
    }

    /// <summary>The near miss of the system tier is ViewUsers, which reads the user directory but not a conversation.</summary>
    [Fact]
    public async Task GetByUserAsync_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(
            view,
            user,
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "Not yours"));

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewUsers).Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).GetByUserAsync(view.Id, user.Id, Ct));
    }

    /// <summary>The near miss is a member of another view asking for someone else's notifications in this one.</summary>
    [Fact]
    public async Task GetByUserAsync_is_forbidden_for_a_caller_holding_ViewView_only_in_another_view()
    {
        var view = TestData.View();
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id);
        await Seed(view, other, otherTeam);

        var caller = Member(other.Id, otherTeam.Id, ViewPermission.ViewView);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).GetByUserAsync(view.Id, Guid.NewGuid(), Ct));
    }

    // ---- Joining ----------------------------------------------------------------------------------

    /// <summary>
    /// A join is answered rather than refused: the reply carries whether it worked and whether the caller
    /// may post, and the hub relays it either way.
    /// </summary>
    [Fact]
    public async Task JoinView_lets_a_caller_with_ManageView_post()
    {
        var view = TestData.View("My View");
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ManageView);
        var notification = await Service(caller).JoinView(view.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
        Assert.Equal("My View", notification.ToName);
        Assert.Equal("Successfully joined My View notifications.", notification.Text);
        Assert.Equal(NotificationPriority.System, notification.Priority);
    }

    /// <summary>
    /// <c>ViewView</c> is a read permission, so it joins but does not broadcast. Broadcasting to everyone in
    /// a view is a management action, and <c>ManageView</c> is what <see cref="INotificationService.PostToView"/>
    /// requires.
    /// </summary>
    [Fact]
    public async Task JoinView_does_not_let_a_caller_with_only_ViewView_post()
    {
        var view = TestData.View("My View");
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);
        var notification = await Service(caller).JoinView(view.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    /// <summary>
    /// A member without <c>ViewView</c> still joins, but read-only.
    /// </summary>
    [Fact]
    public async Task JoinView_lets_a_plain_member_listen_without_posting()
    {
        var view = TestData.View("My View");
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder().WithTeam(view.Id, team.Id).Build();
        var notification = await Service(caller).JoinView(view.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    /// <summary>
    /// The refusal does not name the view. Naming it would tell a caller with no claim on the view that a
    /// view with that id exists, and what it is called, from the guid alone.
    /// </summary>
    [Fact]
    public async Task JoinView_refuses_a_caller_with_no_claim_on_the_view_without_naming_it()
    {
        var view = TestData.View("My View");
        await Seed(view);

        var notification = await Service(ClaimsPrincipalBuilder.Anonymous()).JoinView(view.Id, Ct);

        Assert.False(notification.WasSuccess);
        Assert.False(notification.CanPost);
        Assert.Null(notification.ToName);
        Assert.Equal("Failed to join View notifications.", notification.Text);
    }

    /// <summary>
    /// A join emits no row: the reply is built and returned, so the view's history stays the messages
    /// people actually sent.
    /// </summary>
    [Fact]
    public async Task JoinView_stores_nothing()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Service(Member(view.Id, team.Id)).JoinView(view.Id, Ct);

        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task JoinTeam_lets_a_caller_with_ManageView_post()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ManageView);
        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
        Assert.Equal("Successfully joined Blue Team notifications.", notification.Text);
    }

    [Fact]
    public async Task JoinTeam_does_not_let_a_caller_with_only_ViewView_post()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);
        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    [Fact]
    public async Task JoinTeam_lets_a_team_member_listen_without_posting()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    /// <summary>
    /// The fallback names only <c>ViewTeam</c>, and a claim on another team in the same view does not
    /// carry it to this one.
    /// </summary>
    [Fact]
    public async Task JoinTeam_fails_for_a_caller_with_no_claim_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        var other = TestData.Team(view.Id, "Red Team");
        await Seed(view, team, other);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, other.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.False(notification.WasSuccess);
        Assert.False(notification.CanPost);
        Assert.Null(notification.ToName);
    }

    /// <summary>
    /// A member of one view cannot listen in on a team of another.
    /// </summary>
    [Fact]
    public async Task JoinTeam_fails_for_a_member_of_another_view()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        var team = TestData.Team(view.Id, "Blue Team");
        var mine = TestData.Team(otherView.Id, "Red Team");
        await Seed(view, otherView, team, mine);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(otherView.Id, mine.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.False(notification.WasSuccess);
        Assert.Null(notification.ToName);
    }

    [Fact]
    public async Task JoinTeam_fails_for_a_caller_who_is_in_no_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var notification = await Service(ClaimsPrincipalBuilder.Anonymous()).JoinTeam(team.Id, Ct);

        Assert.False(notification.WasSuccess);
    }

    /// <summary>
    /// The near miss of the view check: ViewView on another view does not observe the team's view, so the
    /// join fails and the reply does not name the team.
    /// </summary>
    [Fact]
    public async Task JoinTeam_refuses_a_caller_holding_ViewView_only_in_another_view()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        var team = TestData.Team(view.Id, "Blue Team");
        var mine = TestData.Team(otherView.Id, "Red Team");
        await Seed(view, otherView, team, mine);

        var notification = await Service(Member(otherView.Id, mine.Id, ViewPermission.ViewView)).JoinTeam(team.Id, Ct);

        Assert.False(notification.WasSuccess);
        Assert.False(notification.CanPost);
        Assert.Null(notification.ToName);
    }

    /// <summary>
    /// The near miss of the team check: ManageTeam on the team is not the ViewTeam the join asks for, so the
    /// join fails and the reply does not name the team.
    /// </summary>
    [Fact]
    public async Task JoinTeam_refuses_a_caller_holding_only_ManageTeam_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.False(notification.WasSuccess);
        Assert.False(notification.CanPost);
        Assert.Null(notification.ToName);
    }

    /// <summary>
    /// A user joins their own conversation within a view; an administrator joins anyone's.
    /// </summary>
    [Fact]
    public async Task JoinUser_lets_a_caller_with_ManageView_post_to_anyone()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ManageView);
        var notification = await Service(caller).JoinUser(view.Id, Guid.NewGuid(), Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
    }

    /// <summary>
    /// <c>ViewView</c> observes the whole view, so it reaches another user's conversation, but read-only.
    /// </summary>
    [Fact]
    public async Task JoinUser_lets_a_caller_with_only_ViewView_listen_to_anyone_without_posting()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);
        var notification = await Service(caller).JoinUser(view.Id, Guid.NewGuid(), Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    [Fact]
    public async Task JoinUser_lets_a_member_join_their_own_conversation()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder().WithTeam(view.Id, team.Id).Build();
        var notification = await Service(caller).JoinUser(view.Id, caller.GetId(), Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    [Fact]
    public async Task JoinUser_refuses_a_member_joining_another_users_conversation()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder().WithTeam(view.Id, team.Id).Build();
        var notification = await Service(caller).JoinUser(view.Id, Guid.NewGuid(), Ct);

        Assert.False(notification.WasSuccess);
    }

    // ---- Posting ----------------------------------------------------------------------------------

    [Fact]
    public async Task PostToView_stores_the_message_addressed_to_the_view()
    {
        var view = TestData.View("My View");
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithName("Poster")
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();
        var posted = await Service(caller).PostToView(view.Id, new Notification { Text = "Hello" }, Ct);

        Assert.True(posted.WasSuccess);
        Assert.True(posted.CanPost);

        await using var db = NewContext();
        var stored = db.Notifications.Single();
        Assert.Equal("Hello", stored.Text);
        Assert.Equal(view.Id, stored.ToId);
        Assert.Equal(NotificationType.View, stored.ToType);
        Assert.Equal("My View", stored.ToName);
        Assert.Equal("Poster", stored.FromName);
        Assert.Equal(caller.GetId(), stored.FromId);
    }

    [Fact]
    public async Task PostToTeam_stores_the_message_addressed_to_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        await Service().PostToTeam(team.Id, new Notification { Text = "Hello" }, Ct);

        await using var db = NewContext();
        var stored = db.Notifications.Single();
        Assert.Equal(team.Id, stored.ToId);
        Assert.Equal(NotificationType.Team, stored.ToType);
        Assert.Equal("Blue Team", stored.ToName);
    }

    [Fact]
    public async Task PostToUser_stores_the_message_addressed_to_the_user_within_a_view()
    {
        var view = TestData.View();
        var user = TestData.User(name: "Recipient");
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        await Service().PostToUser(view.Id, user.Id, new Notification { Text = "Hello" }, Ct);

        await using var db = NewContext();
        var stored = db.Notifications.Single();
        Assert.Equal(user.Id, stored.ToId);
        Assert.Equal(NotificationType.User, stored.ToType);
        Assert.Equal("Recipient", stored.ToName);
        Assert.Equal(view.Id, stored.ViewId);
    }

    /// <summary>
    /// Broadcasting requires <c>ManageView</c>, and the check comes before the write, so a refused post leaves
    /// no row behind. <c>CanPost</c> on a join reply is advisory — the client uses it to decide whether to offer
    /// a compose box — so the same rule has to be enforced here, where the message is actually stored.
    /// </summary>
    [Fact]
    public async Task PostToView_is_forbidden_without_ManageView_and_stores_nothing()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        // A member who may read the view but not manage it.
        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).PostToView(view.Id, new Notification { Text = "Hello" }, Ct));

        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task PostToView_is_forbidden_for_a_caller_holding_ManageView_only_in_another_view()
    {
        var view = TestData.View();
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id);
        await Seed(view, other, otherTeam);

        var caller = Member(other.Id, otherTeam.Id, ViewPermission.ManageView);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).PostToView(view.Id, new Notification { Text = "Hello" }, Ct));

        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task PostToTeam_is_forbidden_without_ManageView_and_stores_nothing()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).PostToTeam(team.Id, new Notification { Text = "Hello" }, Ct));

        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task PostToUser_is_forbidden_without_ManageView_and_stores_nothing()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).PostToUser(view.Id, user.Id, new Notification { Text = "Hello" }, Ct));

        Assert.Empty(await Stored());
    }

    /// <summary>
    /// The recipient has to belong to the view the caller manages: <c>ManageView</c> on one view does not
    /// reach a user outside it, and neither does a system administrator naming that view.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostToUser_rejects_a_user_outside_the_view_and_stores_nothing(bool asViewManager)
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        var service = asViewManager ? Service(Member(view.Id, team.Id, ViewPermission.ManageView)) : Service();

        await Assert.ThrowsAsync<EntityNotFoundException<User>>(
            () => service.PostToUser(view.Id, user.Id, new Notification { Text = "Hello" }, Ct));

        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task PostToUser_rejects_a_user_who_belongs_only_to_another_view()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        var user = TestData.User();
        await Seed(view, otherView, user, TestData.ViewMembership(otherView.Id, user.Id));

        await Assert.ThrowsAsync<EntityNotFoundException<User>>(
            () => Service().PostToUser(view.Id, user.Id, new Notification { Text = "Hello" }, Ct));

        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task PostToUser_rejects_a_user_who_does_not_exist()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<EntityNotFoundException<User>>(
            () => Service().PostToUser(view.Id, Guid.NewGuid(), new Notification { Text = "Hello" }, Ct));

        Assert.Empty(await Stored());
    }

    // ---- Icon url ---------------------------------------------------------------------------------

    /// <summary>
    /// The icon a client shows beside a message. System messages and user messages have separate
    /// configured defaults.
    /// </summary>
    [Fact]
    public async Task Notifications_carry_the_configured_icon_for_their_priority()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var service = Service(Member(view.Id, team.Id, ViewPermission.ManageView));

        var joined = await service.JoinView(view.Id, Ct);
        var posted = await service.PostToView(view.Id, new Notification { Text = "Hello" }, Ct);

        Assert.Equal("https://example.test/system.png", joined.IconUrl);
        Assert.Equal("https://example.test/user.png", posted.IconUrl);
    }

    /// <summary>
    /// A <c>client_logo</c> claim overrides both, so a message shows the branding of the client that sent
    /// it.
    /// </summary>
    [Fact]
    public async Task Notifications_prefer_the_client_logo_claim_over_the_configured_icons()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id)
            .WithClaim("client_logo", "https://example.test/client.png")
            .Build();

        var joined = await Service(caller).JoinView(view.Id, Ct);

        Assert.Equal("https://example.test/client.png", joined.IconUrl);
    }

    // ---- Delete -----------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_removes_the_notification()
    {
        var view = TestData.View();
        var notification = TestData.Notification(view.Id);
        await Seed(view, notification);

        Assert.True(await Service().DeleteAsync(view.Id, notification.Key, Ct));
        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task DeleteAsync_removes_a_user_notification_sent_within_the_view()
    {
        var view = TestData.View();
        var notification = TestData.Notification(Guid.NewGuid(), NotificationType.User, viewId: view.Id);
        await Seed(view, notification);

        Assert.True(await Service().DeleteAsync(view.Id, notification.Key, Ct));
        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task DeleteAsync_removes_a_team_notification_of_a_team_in_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var notification = TestData.Notification(team.Id, NotificationType.Team);
        await Seed(view, team, notification);

        Assert.True(await Service().DeleteAsync(view.Id, notification.Key, Ct));
        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task DeleteAsync_throws_for_an_unknown_key()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<EntityNotFoundException<Notification>>(
            () => Service().DeleteAsync(view.Id, -1, Ct));
    }

    /// <summary>
    /// Keys are sequential, so a key alone names any view's notification. Outside the view the caller
    /// named, the key does not exist.
    /// </summary>
    [Theory]
    [InlineData(NotificationType.View)]
    [InlineData(NotificationType.Team)]
    [InlineData(NotificationType.User)]
    public async Task DeleteAsync_does_not_find_a_notification_of_another_view(NotificationType toType)
    {
        var view = TestData.View();
        var otherView = TestData.View();
        var otherTeam = TestData.Team(otherView.Id);
        var notification = toType switch
        {
            NotificationType.View => TestData.Notification(otherView.Id, NotificationType.View),
            NotificationType.Team => TestData.Notification(otherTeam.Id, NotificationType.Team),
            _ => TestData.Notification(Guid.NewGuid(), NotificationType.User, viewId: otherView.Id),
        };
        await Seed(view, otherView, otherTeam, notification);

        await Assert.ThrowsAsync<EntityNotFoundException<Notification>>(
            () => Service().DeleteAsync(view.Id, notification.Key, Ct));
        Assert.Single(await Stored());
    }

    /// <summary>
    /// A match on the view id as the recipient counts only for a notification addressed to a view, so a
    /// team or user notification whose recipient id happens to equal the view's id is not found.
    /// </summary>
    [Theory]
    [InlineData(NotificationType.Team)]
    [InlineData(NotificationType.User)]
    public async Task DeleteAsync_does_not_find_a_notification_to_another_type_whose_recipient_id_is_the_view_id(
        NotificationType toType)
    {
        var view = TestData.View();
        var lookalike = TestData.Notification(view.Id, toType);
        await Seed(view, lookalike);

        await Assert.ThrowsAsync<EntityNotFoundException<Notification>>(
            () => Service().DeleteAsync(view.Id, lookalike.Key, Ct));
        Assert.Equal(lookalike.Key, Assert.Single(await Stored()).Key);
    }

    [Fact]
    public async Task DeleteViewNotificationsAsync_removes_every_notification_of_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var otherView = TestData.View();
        var otherTeam = TestData.Team(otherView.Id);
        var user = TestData.User();
        var other = TestData.Notification(Guid.NewGuid());
        var otherTeamNotification = TestData.Notification(otherTeam.Id, NotificationType.Team);
        await Seed(
            view,
            team,
            otherView,
            otherTeam,
            user,
            TestData.Notification(view.Id),
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id),
            TestData.Notification(team.Id, NotificationType.Team),
            otherTeamNotification,
            other);

        Assert.True(await Service().DeleteViewNotificationsAsync(view.Id, Ct));
        Assert.Equal(new[] { other.Key, otherTeamNotification.Key }.Order(), (await Stored()).Select(x => x.Key).Order());
    }

    /// <summary>
    /// A match on the view id as the recipient counts only for a notification addressed to a view, so a
    /// team or user notification whose recipient id happens to equal the view's id is not swept.
    /// </summary>
    [Theory]
    [InlineData(NotificationType.Team)]
    [InlineData(NotificationType.User)]
    public async Task DeleteViewNotificationsAsync_leaves_a_notification_to_another_type_whose_recipient_id_is_the_view_id(
        NotificationType toType)
    {
        var view = TestData.View();
        var lookalike = TestData.Notification(view.Id, toType);
        await Seed(view, TestData.Notification(view.Id), lookalike);

        Assert.True(await Service().DeleteViewNotificationsAsync(view.Id, Ct));
        Assert.Equal(lookalike.Key, Assert.Single(await Stored()).Key);
    }

    /// <summary>
    /// False rather than an exception: the view delete that calls this does not care whether there was
    /// anything to sweep.
    /// </summary>
    [Fact]
    public async Task DeleteViewNotificationsAsync_returns_false_when_the_view_has_none()
    {
        Assert.False(await Service().DeleteViewNotificationsAsync(Guid.NewGuid(), Ct));
    }

    // ---- Who may call -----------------------------------------------------------------------------

    /// <summary>ViewTeam on the team itself admits the caller, without a view or system permission.</summary>
    [Fact]
    public async Task GetByTeamAsync_is_allowed_for_a_caller_holding_ViewTeam_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team, TestData.Notification(team.Id, NotificationType.Team, text: "For the team"));

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        var notifications = await Service(caller).GetByTeamAsync(team.Id, Ct);

        Assert.Equal("For the team", Assert.Single(notifications).Text);
    }

    /// <summary>
    /// ViewViews alone admits a caller who is in no view, so the system tier of the read check is the
    /// grant; it reads but does not post.
    /// </summary>
    [Fact]
    public async Task JoinView_lets_a_caller_holding_only_ViewViews_listen_without_posting()
    {
        var view = TestData.View("My View");
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewViews).Build();
        var notification = await Service(caller).JoinView(view.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    [Fact]
    public async Task JoinTeam_lets_a_caller_holding_only_ViewViews_listen_without_posting()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewViews).Build();
        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    /// <summary>
    /// The near miss is ViewView in another view: the caller is a member there and not here, so neither
    /// the read check nor membership admits it, and the refusal does not name the view.
    /// </summary>
    [Fact]
    public async Task JoinView_refuses_a_caller_holding_ViewView_only_in_another_view()
    {
        var view = TestData.View("My View");
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id);
        await Seed(view, other, otherTeam);

        var notification = await Service(Member(other.Id, otherTeam.Id, ViewPermission.ViewView)).JoinView(view.Id, Ct);

        Assert.False(notification.WasSuccess);
        Assert.Null(notification.ToName);
    }

    /// <summary>
    /// The near miss is membership of another view: the caller asks for its own conversation, so only the
    /// membership of the view asked for is missing.
    /// </summary>
    [Fact]
    public async Task JoinUser_refuses_a_caller_joining_its_own_conversation_in_a_view_it_is_not_in()
    {
        var view = TestData.View();
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id);
        await Seed(view, other, otherTeam);

        var caller = new ClaimsPrincipalBuilder().WithTeam(other.Id, otherTeam.Id).Build();
        var notification = await Service(caller).JoinUser(view.Id, caller.GetId(), Ct);

        Assert.False(notification.WasSuccess);
    }

    // ---- One allowed case per permission value ---------------------------------------------------

    [Fact]
    public async Task GetByTeamAsync_is_allowed_for_a_caller_holding_only_ViewViews()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team, TestData.Notification(team.Id, NotificationType.Team, text: "For the team"));

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewViews).Build();

        var notifications = await Service(caller).GetByTeamAsync(team.Id, Ct);

        Assert.Equal("For the team", Assert.Single(notifications).Text);
    }

    /// <summary>The view tier: ViewView on the team's view, held on another team of that view.</summary>
    [Fact]
    public async Task GetByTeamAsync_is_allowed_for_a_caller_holding_ViewView_on_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other Team");
        await Seed(view, team, other, TestData.Notification(team.Id, NotificationType.Team, text: "For the team"));

        var caller = Member(view.Id, other.Id, ViewPermission.ViewView);

        var notifications = await Service(caller).GetByTeamAsync(team.Id, Ct);

        Assert.Equal("For the team", Assert.Single(notifications).Text);
    }

    [Fact]
    public async Task JoinView_lets_a_caller_holding_only_ManageViews_post()
    {
        var view = TestData.View("My View");
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageViews).Build();
        var notification = await Service(caller).JoinView(view.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
        Assert.Equal("Successfully joined My View notifications.", notification.Text);
    }

    [Fact]
    public async Task JoinTeam_lets_a_caller_holding_only_ManageViews_post()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageViews).Build();
        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
        Assert.Equal("Successfully joined Blue Team notifications.", notification.Text);
    }

    [Fact]
    public async Task JoinUser_lets_a_caller_holding_only_ManageViews_post_to_anyone()
    {
        var view = TestData.View();
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageViews).Build();
        var notification = await Service(caller).JoinUser(view.Id, Guid.NewGuid(), Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
    }

    [Fact]
    public async Task JoinUser_lets_a_caller_holding_only_ViewViews_listen_to_anyone_without_posting()
    {
        var view = TestData.View();
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewViews).Build();
        var notification = await Service(caller).JoinUser(view.Id, Guid.NewGuid(), Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    [Fact]
    public async Task PostToView_is_allowed_for_a_caller_holding_only_ManageViews()
    {
        var view = TestData.View("My View");
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageViews).Build();
        await Service(caller).PostToView(view.Id, new Notification { Text = "Hello" }, Ct);

        var stored = await ReadBack(db => db.Notifications.SingleAsync(x => x.ToId == view.Id, Ct));
        Assert.Equal("Hello", stored.Text);
        Assert.Equal(caller.GetId(), stored.FromId);
    }

    [Fact]
    public async Task PostToTeam_is_allowed_for_a_caller_holding_only_ManageViews()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageViews).Build();
        await Service(caller).PostToTeam(team.Id, new Notification { Text = "Hello" }, Ct);

        var stored = await ReadBack(db => db.Notifications.SingleAsync(x => x.ToId == team.Id, Ct));
        Assert.Equal("Hello", stored.Text);
        Assert.Equal(caller.GetId(), stored.FromId);
    }

    /// <summary>The view tier: ManageView on the view, held on a team of that view.</summary>
    [Fact]
    public async Task PostToUser_is_allowed_for_a_caller_holding_ManageView_on_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User(name: "Recipient");
        await Seed(view, team, user, TestData.ViewMembership(view.Id, user.Id));

        var caller = Member(view.Id, team.Id, ViewPermission.ManageView);
        await Service(caller).PostToUser(view.Id, user.Id, new Notification { Text = "Hello" }, Ct);

        var stored = await ReadBack(db => db.Notifications.SingleAsync(x => x.ToId == user.Id, Ct));
        Assert.Equal("Hello", stored.Text);
        Assert.Equal(view.Id, stored.ViewId);
    }

    // ---- One near miss per tier -------------------------------------------------------------------

    /// <summary>The system tier: ManageViews where reading a team's notifications takes ViewViews.</summary>
    [Fact]
    public async Task GetByTeamAsync_is_forbidden_for_a_caller_holding_only_ManageViews()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageViews).Build();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(caller).GetByTeamAsync(team.Id, Ct));
    }

    /// <summary>The view tier: ViewView in another view, where the team's own view is the one checked.</summary>
    [Fact]
    public async Task GetByTeamAsync_is_forbidden_for_a_caller_holding_ViewView_only_in_another_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id, "Other Team");
        await Seed(view, team, other, otherTeam);

        var caller = Member(other.Id, otherTeam.Id, ViewPermission.ViewView);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(caller).GetByTeamAsync(team.Id, Ct));
    }

    /// <summary>The system tier: ViewViews where posting to a view takes ManageViews; nothing is stored.</summary>
    [Fact]
    public async Task PostToView_is_forbidden_for_a_caller_holding_only_ViewViews_and_stores_nothing()
    {
        var view = TestData.View();
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewViews).Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).PostToView(view.Id, new Notification { Text = "Hello" }, Ct));
        Assert.Empty(await Stored());
    }

    /// <summary>The system tier: ViewViews where posting to a team takes ManageViews; nothing is stored.</summary>
    [Fact]
    public async Task PostToTeam_is_forbidden_for_a_caller_holding_only_ViewViews_and_stores_nothing()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewViews).Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).PostToTeam(team.Id, new Notification { Text = "Hello" }, Ct));
        Assert.Empty(await Stored());
    }

    /// <summary>The system tier: ViewViews where posting to a user takes ManageViews; nothing is stored.</summary>
    [Fact]
    public async Task PostToUser_is_forbidden_for_a_caller_holding_only_ViewViews_and_stores_nothing()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewViews).Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(caller).PostToUser(view.Id, user.Id, new Notification { Text = "Hello" }, Ct));
        Assert.Empty(await Stored());
    }

    /// <summary>
    /// The system tier of both join checks: ViewUsers, a system permission neither accepts, joins nothing
    /// and is not offered posting.
    /// </summary>
    [Fact]
    public async Task JoinView_refuses_a_caller_holding_only_ViewUsers()
    {
        var view = TestData.View("My View");
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewUsers).Build();
        var notification = await Service(caller).JoinView(view.Id, Ct);

        Assert.False(notification.WasSuccess);
        Assert.False(notification.CanPost);
        Assert.Equal("Failed to join View notifications.", notification.Text);
    }

    /// <summary>
    /// The system tier of both join checks: ViewUsers, a system permission neither accepts, does not join
    /// another user's conversation and is not offered posting.
    /// </summary>
    [Fact]
    public async Task JoinUser_refuses_a_caller_holding_only_ViewUsers()
    {
        var view = TestData.View();
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewUsers).Build();
        var notification = await Service(caller).JoinUser(view.Id, Guid.NewGuid(), Ct);

        Assert.False(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    /// <summary>
    /// The view tier of both join checks: ViewView in another view does not join a conversation in this
    /// one and is not offered posting.
    /// </summary>
    [Fact]
    public async Task JoinUser_refuses_a_caller_holding_ViewView_only_in_another_view()
    {
        var view = TestData.View();
        var other = TestData.View("Other View");
        var otherTeam = TestData.Team(other.Id, "Other Team");
        await Seed(view, other, otherTeam);

        var caller = Member(other.Id, otherTeam.Id, ViewPermission.ViewView);
        var notification = await Service(caller).JoinUser(view.Id, Guid.NewGuid(), Ct);

        Assert.False(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// The notifications as they are on disk, read through a context that lives only for the read: the
    /// service writes through the context the test holds, and an undisposed context keeps its pooled
    /// connection for the rest of the run.
    /// </summary>
    private async Task<List<NotificationEntity>> Stored()
    {
        await using var db = NewContext();

        return await db.Notifications.AsNoTracking().ToListAsync(Ct);
    }

    private INotificationService Service(ClaimsPrincipal user = null) =>
        HostFor(user ?? Root).Resolve<INotificationService>();

    /// <summary>
    /// A caller whose only claim is on one team of one view — the shape every membership-based read here
    /// is checking for.
    /// </summary>
    private static ClaimsPrincipal Member(Guid viewId, Guid teamId, params ViewPermission[] viewPermissions) =>
        new ClaimsPrincipalBuilder()
            .WithTeam(viewId, teamId, viewPermissions: viewPermissions)
            .Build();
}
