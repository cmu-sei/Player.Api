// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Users;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Users;

/// <summary>
/// Covers the <c>Users</c> feature's request handlers.
/// </summary>
public class UserRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_user_and_returns_it()
    {
        var created = await SendAsync(new Create.Command { Id = Guid.NewGuid(), Name = "New Person" });

        Assert.Equal("New Person", created.Name);

        await using var db = NewContext();
        Assert.True(await db.Users.AnyAsync(x => x.Id == created.Id, Ct));
    }

    [Fact]
    public async Task Create_assigns_the_named_role()
    {
        var role = await Db.Roles.AsNoTracking().FirstAsync(Ct);

        var created = await SendAsync(new Create.Command
        {
            Id = Guid.NewGuid(),
            Name = "With a role",
            RoleId = role.Id
        });

        Assert.Equal(role.Name, created.RoleName);
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageUsers()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Create.Command { Id = Guid.NewGuid(), Name = "Nope" }));
    }

    // ---- Get / GetAll ---------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_user()
    {
        var user = TestData.User(name: "Findable");
        await Seed(user);

        Assert.Equal("Findable", (await SendAsync(new Get.Query { Id = user.Id })).Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_user_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<User>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Get_is_allowed_for_the_caller_asking_about_themselves()
    {
        var builder = new ClaimsPrincipalBuilder();
        await Seed(TestData.User(builder.UserId, "Me"));

        Assert.Equal("Me", (await SendAsync(builder.Build(), new Get.Query { Id = builder.UserId })).Name);
    }

    /// <summary>
    /// The fallback branch: a caller with no user-level permission may still read a user they share a
    /// team with, provided they hold <c>ViewTeam</c> on it.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ViewTeam_on_a_team_the_user_belongs_to()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var subject = TestData.User(name: "Teammate");
        var viewMembership = TestData.ViewMembership(view.Id, subject.Id);
        await Seed(
            view, team, subject, viewMembership,
            TestData.TeamMembership(team.Id, subject.Id, viewMembership.Id));

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        Assert.Equal("Teammate", (await SendAsync(caller, new Get.Query { Id = subject.Id })).Name);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_sharing_no_team_with_the_user()
    {
        var user = TestData.User();
        await Seed(user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new Get.Query { Id = user.Id }));
    }

    [Fact]
    public async Task GetAll_returns_every_user()
    {
        await Seed(TestData.User(name: "One"), TestData.User(name: "Two"));

        var users = await SendAsync(new GetAll.Query());

        Assert.Equal(2, users.Length);
    }

    [Fact]
    public async Task GetAll_is_forbidden_without_ViewUsers()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new GetAll.Query()));
    }

    // ---- GetByTeam / GetByView ------------------------------------------------------------------

    [Fact]
    public async Task GetByTeam_returns_the_team_members()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var other = TestData.Team(view.Id, "Other");
        var member = TestData.User(name: "Member");
        var stranger = TestData.User(name: "Stranger");
        var memberView = TestData.ViewMembership(view.Id, member.Id);
        var strangerView = TestData.ViewMembership(view.Id, stranger.Id);
        await Seed(
            view, team, other, member, stranger, memberView, strangerView,
            TestData.TeamMembership(team.Id, member.Id, memberView.Id),
            TestData.TeamMembership(other.Id, stranger.Id, strangerView.Id));

        var users = await SendAsync(new GetByTeam.Query { TeamId = team.Id });

        Assert.Equal("Member", Assert.Single(users).Name);
    }

    [Fact]
    public async Task GetByTeam_reports_a_missing_team_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new GetByTeam.Query { TeamId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task GetByView_returns_the_view_members()
    {
        var view = TestData.View();
        var other = TestData.View("Other");
        var member = TestData.User(name: "Member");
        var stranger = TestData.User(name: "Stranger");
        await Seed(
            view, other, member, stranger,
            TestData.ViewMembership(view.Id, member.Id),
            TestData.ViewMembership(other.Id, stranger.Id));

        var users = await SendAsync(new GetByView.Query { ViewId = view.Id });

        Assert.Equal("Member", Assert.Single(users).Name);
    }

    [Fact]
    public async Task GetByView_reports_a_missing_view_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Views.View>>(
            () => SendAsync(new GetByView.Query { ViewId = Guid.NewGuid() }));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_renames_the_user()
    {
        var user = TestData.User(name: "Before");
        await Seed(user);

        Assert.Equal("After", (await SendAsync(new Edit.Command { Id = user.Id, Name = "After" })).Name);
    }

    [Fact]
    public async Task Edit_reports_a_missing_user_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<User>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid(), Name = "Ghost" }));
    }

    [Fact]
    public async Task Edit_is_forbidden_without_ManageUsers()
    {
        var user = TestData.User();
        await Seed(user);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Edit.Command { Id = user.Id, Name = "Nope" }));
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_user()
    {
        var user = TestData.User();
        await Seed(user);

        await SendAsync(new Delete.Command { Id = user.Id });

        await using var db = NewContext();
        Assert.False(await db.Users.AnyAsync(x => x.Id == user.Id, Ct));
    }

    /// <summary>
    /// Refused before the lookup, so it holds even for a caller with no user row.
    /// </summary>
    [Fact]
    public async Task Delete_refuses_to_delete_the_caller_own_account()
    {
        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(new Delete.Command { Id = RootHost.UserId }));

        Assert.Contains("your own account", exception.Message);
    }

    [Fact]
    public async Task Delete_reports_a_missing_user_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<User>>(
            () => SendAsync(new Delete.Command { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Delete_is_forbidden_without_ManageUsers()
    {
        var user = TestData.User();
        await Seed(user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new Delete.Command { Id = user.Id }));
    }

    // ---- AddToTeam ------------------------------------------------------------------------------

    /// <summary>
    /// A first team in a view brings a view membership with it, and that membership becomes primary —
    /// there is nothing else for it to point at.
    /// </summary>
    [Fact]
    public async Task AddToTeam_creates_the_view_membership_and_makes_it_primary()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        await SendAsync(new AddToTeam.Command { TeamId = team.Id, UserId = user.Id });

        await using var db = NewContext();
        var viewMembership = await db.ViewMemberships.SingleAsync(x => x.UserId == user.Id, Ct);
        var teamMembership = await db.TeamMemberships.SingleAsync(x => x.UserId == user.Id, Ct);
        Assert.Equal(view.Id, viewMembership.ViewId);
        Assert.Equal(team.Id, teamMembership.TeamId);
        Assert.Equal(teamMembership.Id, viewMembership.PrimaryTeamMembershipId);
    }

    /// <summary>
    /// A second team in the same view reuses the existing view membership and leaves the primary alone.
    /// </summary>
    [Fact]
    public async Task AddToTeam_reuses_an_existing_view_membership()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "First");
        var second = TestData.Team(view.Id, "Second");
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var firstMembership = TestData.TeamMembership(first.Id, user.Id, viewMembership.Id);
        await Seed(view, first, second, user, viewMembership, firstMembership);
        viewMembership.PrimaryTeamMembershipId = firstMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await SendAsync(new AddToTeam.Command { TeamId = second.Id, UserId = user.Id });

        await using var db = NewContext();
        Assert.Single(await db.ViewMemberships.Where(x => x.UserId == user.Id).ToListAsync(Ct));
        Assert.Equal(2, await db.TeamMemberships.CountAsync(x => x.UserId == user.Id, Ct));
        Assert.Equal(
            firstMembership.Id,
            (await db.ViewMemberships.SingleAsync(x => x.Id == viewMembership.Id, Ct)).PrimaryTeamMembershipId);
    }

    [Fact]
    public async Task AddToTeam_reports_a_missing_team_as_not_found()
    {
        var user = TestData.User();
        await Seed(user);

        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new AddToTeam.Command { TeamId = Guid.NewGuid(), UserId = user.Id }));
    }

    [Fact]
    public async Task AddToTeam_reports_a_missing_user_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<EntityNotFoundException<User>>(
            () => SendAsync(new AddToTeam.Command { TeamId = team.Id, UserId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task AddToTeam_is_forbidden_without_ManageTeam()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            caller,
            new AddToTeam.Command { TeamId = team.Id, UserId = user.Id }));
    }

    // ---- RemoveFromTeam -------------------------------------------------------------------------

    /// <summary>
    /// Removing the user's only team in a view removes the view membership too, since a view membership
    /// with no team membership is not a state the model allows.
    /// </summary>
    [Fact]
    public async Task RemoveFromTeam_removes_the_view_membership_along_with_the_last_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(view, team, user, viewMembership, teamMembership);
        viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await SendAsync(new RemoveFromTeam.Command { TeamId = team.Id, UserId = user.Id });

        await using var db = NewContext();
        Assert.False(await db.TeamMemberships.AnyAsync(x => x.UserId == user.Id, Ct));
        Assert.False(await db.ViewMemberships.AnyAsync(x => x.UserId == user.Id, Ct));
    }

    /// <summary>
    /// Removing the primary team moves the primary to another of the user's teams in that view, rather
    /// than leaving the membership pointing at a deleted row.
    /// </summary>
    [Fact]
    public async Task RemoveFromTeam_moves_the_primary_when_it_is_the_team_being_removed()
    {
        var view = TestData.View();
        var primary = TestData.Team(view.Id, "Primary");
        var secondary = TestData.Team(view.Id, "Secondary");
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var primaryMembership = TestData.TeamMembership(primary.Id, user.Id, viewMembership.Id);
        var secondaryMembership = TestData.TeamMembership(secondary.Id, user.Id, viewMembership.Id);
        await Seed(view, primary, secondary, user, viewMembership, primaryMembership, secondaryMembership);
        viewMembership.PrimaryTeamMembershipId = primaryMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await SendAsync(new RemoveFromTeam.Command { TeamId = primary.Id, UserId = user.Id });

        await using var db = NewContext();
        Assert.Equal(
            secondaryMembership.Id,
            (await db.ViewMemberships.SingleAsync(x => x.Id == viewMembership.Id, Ct)).PrimaryTeamMembershipId);
        Assert.Equal(
            secondary.Id,
            (await db.TeamMemberships.SingleAsync(x => x.UserId == user.Id, Ct)).TeamId);
    }

    [Fact]
    public async Task RemoveFromTeam_leaves_the_primary_alone_when_removing_another_team()
    {
        var view = TestData.View();
        var primary = TestData.Team(view.Id, "Primary");
        var secondary = TestData.Team(view.Id, "Secondary");
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var primaryMembership = TestData.TeamMembership(primary.Id, user.Id, viewMembership.Id);
        await Seed(
            view, primary, secondary, user, viewMembership, primaryMembership,
            TestData.TeamMembership(secondary.Id, user.Id, viewMembership.Id));
        viewMembership.PrimaryTeamMembershipId = primaryMembership.Id;
        await Db.SaveChangesAsync(Ct);

        await SendAsync(new RemoveFromTeam.Command { TeamId = secondary.Id, UserId = user.Id });

        await using var db = NewContext();
        Assert.Equal(
            primaryMembership.Id,
            (await db.ViewMemberships.SingleAsync(x => x.Id == viewMembership.Id, Ct)).PrimaryTeamMembershipId);
    }

    /// <summary>
    /// A user who is not on the team is not an error — the request is already satisfied.
    /// </summary>
    [Fact]
    public async Task RemoveFromTeam_does_nothing_when_the_user_is_not_a_member()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        await SendAsync(new RemoveFromTeam.Command { TeamId = team.Id, UserId = user.Id });

        await using var db = NewContext();
        Assert.False(await db.TeamMemberships.AnyAsync(Ct));
    }

    [Fact]
    public async Task RemoveFromTeam_reports_a_missing_team_as_not_found()
    {
        var user = TestData.User();
        await Seed(user);

        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new RemoveFromTeam.Command { TeamId = Guid.NewGuid(), UserId = user.Id }));
    }

    [Fact]
    public async Task RemoveFromTeam_reports_a_missing_user_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<EntityNotFoundException<User>>(
            () => SendAsync(new RemoveFromTeam.Command { TeamId = team.Id, UserId = Guid.NewGuid() }));
    }

    // ---- SendNotification -----------------------------------------------------------------------

    [Fact]
    public async Task SendNotification_persists_it_and_broadcasts_to_the_user_group()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        var result = await SendAsync(new SendNotification.Command
        {
            ViewId = view.Id,
            UserId = user.Id,
            Subject = "For you",
            Text = "Something happened"
        });

        Assert.Contains(user.Id.ToString(), result);

        await using var db = NewContext();
        var notification = await db.Notifications.SingleAsync(Ct);
        Assert.Equal(user.Id, notification.ToId);
        Assert.Equal(NotificationType.User, notification.ToType);

        RootHost.UserHub.Clients.Received().Group($"{view.Id}_{user.Id}");
    }

    [Fact]
    public async Task SendNotification_rejects_a_notification_with_no_text()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        await Assert.ThrowsAsync<ArgumentException>(() => SendAsync(new SendNotification.Command
        {
            ViewId = view.Id,
            UserId = user.Id,
            Subject = "No body"
        }));
    }

    /// <summary>
    /// Characterizes current behaviour, which is not the intended one. This handler authorizes against
    /// <c>UserEntity</c>, a type <c>AuthorizationService.GetResourceResult</c> does not handle, so every
    /// caller without <c>ManageViews</c> — including the view administrators the call's
    /// <c>ManageView</c> argument was written for — gets a <c>NotImplementedException</c> rather than a
    /// decision.
    /// </summary>
    [Fact]
    public async Task SendNotification_throws_for_a_caller_without_ManageViews()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        var exception = await Assert.ThrowsAsync<NotImplementedException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new SendNotification.Command { ViewId = view.Id, UserId = user.Id, Text = "Nope" }));

        Assert.Contains("UserEntity", exception.Message);
    }
}
