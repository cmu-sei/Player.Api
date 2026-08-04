// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.Tests.Support;
using Notification = Player.Api.ViewModels.Notification;

namespace Player.Api.Tests.Services;

/// <summary>
/// The chat and system-message service behind the SignalR hubs. Every join returns a notification
/// describing whether it worked, so the interesting behaviour is in what that reply says rather than in
/// what it throws.
/// </summary>
public class NotificationServiceTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ---- Reads ------------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_returns_every_notification()
    {
        var view = TestData.View();
        await Seed(view, TestData.Notification(view.Id), TestData.Notification(Guid.NewGuid()));

        Assert.Equal(2, (await Service().GetAsync(Ct)).Count());
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

    [Fact]
    public async Task GetAsync_is_forbidden_without_ViewViews()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(ClaimsPrincipalBuilder.Anonymous()).GetAsync(Ct));
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
    /// Unlike the others this one authorizes nothing — the request handler does it, and the sweep behind
    /// a view delete needs every message including the system ones.
    /// </summary>
    [Fact]
    public async Task GetAllViewNotificationsAsync_returns_everything_addressed_to_or_within_the_view()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(
            view,
            user,
            TestData.Notification(view.Id, priority: NotificationPriority.System, text: "Joined"),
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "To a member"),
            TestData.Notification(Guid.NewGuid(), text: "Elsewhere"));

        var notifications = await Service(ClaimsPrincipalBuilder.Anonymous())
            .GetAllViewNotificationsAsync(view.Id, Ct);

        Assert.Equal(["Joined", "To a member"], notifications.Select(x => x.Text).Order());
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
    public async Task GetByTeamAsync_is_forbidden_without_access_to_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(ClaimsPrincipalBuilder.Anonymous()).GetByTeamAsync(team.Id, Ct));
    }

    [Fact]
    public async Task GetByUserAsync_returns_the_users_notifications()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(
            view,
            team,
            user,
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id, text: "For the user"),
            TestData.Notification(user.Id, NotificationType.User, priority: NotificationPriority.System));

        var notifications = await Service(Member(view.Id, team.Id)).GetByUserAsync(view.Id, user.Id, Ct);

        Assert.Equal("For the user", Assert.Single(notifications).Text);
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

    [Fact]
    public async Task GetByUserAsync_is_forbidden_for_another_users_notifications()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(ClaimsPrincipalBuilder.Anonymous()).GetByUserAsync(view.Id, Guid.NewGuid(), Ct));
    }

    // ---- Joining ----------------------------------------------------------------------------------

    /// <summary>
    /// A join is answered rather than refused: the reply carries whether it worked and whether the caller
    /// may post, and the hub relays it either way.
    /// </summary>
    [Fact]
    public async Task JoinView_lets_a_caller_with_ViewView_post()
    {
        var view = TestData.View("My View");
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);
        var notification = await Service(caller).JoinView(view.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
        Assert.Equal("My View", notification.ToName);
        Assert.Equal("Successfully joined My View notifications.", notification.Text);
        Assert.Equal(NotificationPriority.System, notification.Priority);
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

    [Fact]
    public async Task JoinView_refuses_a_caller_with_no_claim_on_the_view()
    {
        var view = TestData.View("My View");
        await Seed(view);

        var notification = await Service(ClaimsPrincipalBuilder.Anonymous()).JoinView(view.Id, Ct);

        Assert.False(notification.WasSuccess);
        Assert.Equal("Failed to join My View notifications.", notification.Text);
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

        Assert.Empty(NewContext().Notifications);
    }

    [Fact]
    public async Task JoinTeam_lets_a_caller_with_ViewView_post()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);
        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
        Assert.Equal("Successfully joined Blue Team notifications.", notification.Text);
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
    /// Characterizes current behaviour. The fallback check passes an empty required-system-permission
    /// array, which the system handler reads as "nothing required" and succeeds — so the
    /// <c>ViewTeam</c> check never runs and no caller is refused. Flip to <c>False</c> once the handler
    /// stops treating empty as allow.
    /// </summary>
    [Fact]
    public async Task JoinTeam_succeeds_for_a_caller_with_no_claim_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        var other = TestData.Team(view.Id, "Red Team");
        await Seed(view, team, other);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, other.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        var notification = await Service(caller).JoinTeam(team.Id, Ct);

        Assert.True(notification.WasSuccess);
        Assert.False(notification.CanPost);
    }

    /// <summary>
    /// Characterizes current behaviour. Same cause as above, at its widest: a caller in no view at all
    /// still joins any team's group. Flip to <c>False</c> alongside the test above.
    /// </summary>
    [Fact]
    public async Task JoinTeam_succeeds_for_a_caller_who_is_in_no_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Blue Team");
        await Seed(view, team);

        var notification = await Service(ClaimsPrincipalBuilder.Anonymous()).JoinTeam(team.Id, Ct);

        Assert.True(notification.WasSuccess);
    }

    /// <summary>
    /// A user joins their own conversation within a view; an administrator joins anyone's.
    /// </summary>
    [Fact]
    public async Task JoinUser_lets_a_caller_with_ViewView_post_to_anyone()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = Member(view.Id, team.Id, ViewPermission.ViewView);
        var notification = await Service(caller).JoinUser(view.Id, Guid.NewGuid(), Ct);

        Assert.True(notification.WasSuccess);
        Assert.True(notification.CanPost);
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
        await Seed(view);

        var caller = new ClaimsPrincipalBuilder().WithName("Poster").Build();
        var posted = await Service(caller).PostToView(view.Id, new Notification { Text = "Hello" }, Ct);

        Assert.True(posted.WasSuccess);
        Assert.True(posted.CanPost);

        using var db = NewContext();
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

        using var db = NewContext();
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
        await Seed(view, user);

        await Service().PostToUser(view.Id, user.Id, new Notification { Text = "Hello" }, Ct);

        using var db = NewContext();
        var stored = db.Notifications.Single();
        Assert.Equal(user.Id, stored.ToId);
        Assert.Equal(NotificationType.User, stored.ToType);
        Assert.Equal("Recipient", stored.ToName);
        Assert.Equal(view.Id, stored.ViewId);
    }

    /// <summary>
    /// Characterizes issue 34: none of the three post methods authorizes the caller. A principal with no claim
    /// on the view broadcasts to it and is told it succeeded — <c>CanPost</c>, the flag a join returns to say
    /// whether posting is allowed, is advisory and never consulted here.
    /// </summary>
    [Fact]
    public async Task PostToView_does_not_check_that_the_caller_may_post()
    {
        var view = TestData.View();
        await Seed(view);

        var posted = await Service(ClaimsPrincipalBuilder.Anonymous())
            .PostToView(view.Id, new Notification { Text = "Hello" }, Ct);

        Assert.True(posted.WasSuccess);
        Assert.True(posted.CanPost);
        Assert.Equal("Hello", NewContext().Notifications.Single().Text);
    }

    [Fact]
    public async Task PostToTeam_does_not_check_that_the_caller_may_post()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var posted = await Service(ClaimsPrincipalBuilder.Anonymous())
            .PostToTeam(team.Id, new Notification { Text = "Hello" }, Ct);

        Assert.True(posted.WasSuccess);
        Assert.Equal("Hello", NewContext().Notifications.Single().Text);
    }

    [Fact]
    public async Task PostToUser_does_not_check_that_the_caller_may_post()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user);

        var posted = await Service(ClaimsPrincipalBuilder.Anonymous())
            .PostToUser(view.Id, user.Id, new Notification { Text = "Hello" }, Ct);

        Assert.True(posted.WasSuccess);
        Assert.Equal("Hello", NewContext().Notifications.Single().Text);
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

        var service = Service(Member(view.Id, team.Id));

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

        Assert.True(await Service().DeleteAsync(notification.Key, Ct));
        Assert.Empty(NewContext().Notifications);
    }

    [Fact]
    public async Task DeleteAsync_throws_for_an_unknown_key()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Notification>>(
            () => Service().DeleteAsync(-1, Ct));
    }

    [Fact]
    public async Task DeleteViewNotificationsAsync_removes_every_notification_of_the_view()
    {
        var view = TestData.View();
        var user = TestData.User();
        var other = TestData.Notification(Guid.NewGuid());
        await Seed(
            view,
            user,
            TestData.Notification(view.Id),
            TestData.Notification(user.Id, NotificationType.User, viewId: view.Id),
            other);

        Assert.True(await Service().DeleteViewNotificationsAsync(view.Id, Ct));
        Assert.Equal(other.Key, NewContext().Notifications.Single().Key);
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

    // ---- Helpers ----------------------------------------------------------------------------------

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
