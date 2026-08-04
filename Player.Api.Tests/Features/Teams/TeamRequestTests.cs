// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Teams;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.Teams;

/// <summary>
/// Covers the <c>Teams</c> feature's request handlers.
/// </summary>
public class TeamRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ---- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_team_in_the_named_view()
    {
        var view = TestData.View();
        await Seed(view);

        var created = await SendAsync(new Create.Command { ViewId = view.Id, Name = "Blue" });

        Assert.Equal("Blue", created.Name);
        Assert.Equal(view.Id, created.ViewId);

        await using var db = NewContext();
        Assert.Equal("Blue", (await db.Teams.SingleAsync(x => x.Id == created.Id, Ct)).Name);
    }

    /// <summary>
    /// <see cref="TeamEntity.RoleId"/> is required, so an unnamed role falls back to
    /// <c>RoleOptions.DefaultTeamRole</c>.
    /// </summary>
    [Fact]
    public async Task Create_falls_back_to_the_configured_default_role()
    {
        var view = TestData.View();
        await Seed(view);

        var created = await SendAsync(new Create.Command { ViewId = view.Id, Name = "Defaulted" });

        Assert.Equal("View Member", created.RoleName);
    }

    [Fact]
    public async Task Create_uses_the_role_the_caller_named()
    {
        var view = TestData.View();
        await Seed(view);

        var created = await SendAsync(new Create.Command
        {
            ViewId = view.Id,
            Name = "Admins",
            RoleId = TestData.TeamRoles.ViewAdmin
        });

        Assert.Equal(TestData.TeamRoles.ViewAdmin, created.RoleId);
    }

    [Fact]
    public async Task Create_honors_a_caller_supplied_id()
    {
        var view = TestData.View();
        await Seed(view);
        var id = Guid.NewGuid();

        var created = await SendAsync(new Create.Command { ViewId = view.Id, Name = "Fixed", Id = id });

        Assert.Equal(id, created.Id);
    }

    [Fact]
    public async Task Create_assigns_an_id_when_the_caller_sends_an_empty_one()
    {
        var view = TestData.View();
        await Seed(view);

        var created = await SendAsync(new Create.Command
        {
            ViewId = view.Id,
            Name = "Empty id",
            Id = Guid.Empty
        });

        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task Create_reports_a_missing_view_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Views.View>>(
            () => SendAsync(new Create.Command { ViewId = Guid.NewGuid(), Name = "Orphan" }));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_view_member_without_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ViewView])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(user, new Create.Command { ViewId = view.Id, Name = "Nope" }));
    }

    // ---- Get / GetAll / GetByView ---------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_team_with_its_role_name()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Readable", TestData.TeamRoles.Observer);
        await Seed(view, team);

        var result = await SendAsync(new Get.Query { Id = team.Id });

        Assert.Equal("Readable", result.Name);
        Assert.Equal("Observer", result.RoleName);
    }

    /// <summary>
    /// Authorization resolves the team to its view first, so a missing team reads as forbidden to
    /// everyone but a system-permission holder.
    /// </summary>
    [Fact]
    public async Task Get_reports_a_missing_team_as_not_found_for_a_system_permission_holder()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Team>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permission_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new Get.Query { Id = team.Id }));
    }

    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ViewTeam_on_that_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        Assert.Equal(team.Id, (await SendAsync(user, new Get.Query { Id = team.Id })).Id);
    }

    [Fact]
    public async Task GetAll_returns_every_team_across_every_view()
    {
        var first = TestData.View("First");
        var second = TestData.View("Second");
        await Seed(first, second, TestData.Team(first.Id, "A"), TestData.Team(second.Id, "B"));

        var teams = await SendAsync(new GetAll.Query());

        Assert.Equal(2, teams.Length);
    }

    [Fact]
    public async Task GetByView_returns_only_that_view_teams()
    {
        var view = TestData.View();
        var other = TestData.View("Other");
        await Seed(view, other, TestData.Team(view.Id, "Mine"), TestData.Team(other.Id, "Theirs"));

        var teams = await SendAsync(new GetByView.Query { ViewId = view.Id });

        Assert.Equal("Mine", Assert.Single(teams).Name);
    }

    [Fact]
    public async Task GetByView_reports_a_missing_view_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Views.View>>(
            () => SendAsync(new GetByView.Query { ViewId = Guid.NewGuid() }));
    }

    // ---- GetByUserView --------------------------------------------------------------------------

    /// <summary>
    /// The privileged branch: an administrator asking about someone else sees every team in the view,
    /// not only the ones that user belongs to.
    /// </summary>
    /// <remarks>
    /// Two of the handler's three branches are covered because only two are reachable. Its
    /// <c>Authorize</c> admits exactly the callers those two match, so the third throws
    /// <c>ForbiddenException</c> before <c>HandleRequest</c> runs.
    /// </remarks>
    [Fact]
    public async Task GetByUserView_returns_every_team_in_the_view_for_a_privileged_caller()
    {
        var view = TestData.View();
        var member = TestData.Team(view.Id, "Member of");
        var notMember = TestData.Team(view.Id, "Not a member of");
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(
            view, member, notMember, user, viewMembership,
            TestData.TeamMembership(member.Id, user.Id, viewMembership.Id));

        var teams = await SendAsync(new GetByUserView.Query { ViewId = view.Id, UserId = user.Id });

        Assert.Equal(2, teams.Length);
    }

    /// <summary>
    /// The self branch: a caller asking about themselves sees what their primary team's permissions
    /// make visible, which is the scoped-permission rule rather than plain membership.
    /// </summary>
    [Fact]
    public async Task GetByUserView_uses_the_primary_visibility_context_for_the_caller_themselves()
    {
        var view = TestData.View();
        var own = TestData.Team(view.Id, "Own");
        var scoped = TestData.Team(view.Id, "Scoped onto");
        var invisible = TestData.Team(view.Id, "Invisible");

        var builder = new ClaimsPrincipalBuilder();
        var user = TestData.User(builder.UserId);
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(
            view, own, scoped, invisible, user, viewMembership,
            TestData.TeamMembership(own.Id, user.Id, viewMembership.Id));

        var caller = builder
            .WithTeam(view.Id, own.Id, isPrimary: true, teamPermissions: [TeamPermission.ViewTeam])
            .WithScopedTeam(
                view.Id,
                scoped.Id,
                sourceTeamIds: [own.Id],
                teamPermissions: [TeamPermission.ViewTeam],
                directTeamPermissions: [])
            .Build();

        var teams = await SendAsync(caller, new GetByUserView.Query { ViewId = view.Id, UserId = user.Id });

        Assert.Equal(["Own", "Scoped onto"], teams.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetByUserView_reports_a_missing_view_as_not_found()
    {
        var user = TestData.User();
        await Seed(user);

        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Views.View>>(
            () => SendAsync(new GetByUserView.Query { ViewId = Guid.NewGuid(), UserId = user.Id }));
    }

    [Fact]
    public async Task GetByUserView_reports_a_missing_user_as_not_found()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Users.User>>(
            () => SendAsync(new GetByUserView.Query { ViewId = view.Id, UserId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task GetByUserView_is_forbidden_for_another_user_without_view_access()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetByUserView.Query { ViewId = view.Id, UserId = user.Id }));
    }

    // ---- Edit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_renames_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Before");
        await Seed(view, team);

        var edited = await SendAsync(new Edit.Command { Id = team.Id, Name = "After" });

        Assert.Equal("After", edited.Name);
    }

    [Fact]
    public async Task Edit_changes_the_role()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, roleId: TestData.TeamRoles.ViewMember);
        await Seed(view, team);

        var edited = await SendAsync(new Edit.Command
        {
            Id = team.Id,
            Name = team.Name,
            RoleId = TestData.TeamRoles.ViewAdmin
        });

        Assert.Equal(TestData.TeamRoles.ViewAdmin, edited.RoleId);
        Assert.Equal("View Admin", edited.RoleName);
    }

    /// <summary>
    /// The global null-source convention at work: a null <c>RoleId</c> leaves the existing role rather
    /// than clearing a required column.
    /// </summary>
    [Fact]
    public async Task Edit_leaves_the_role_alone_when_the_caller_omits_it()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, roleId: TestData.TeamRoles.Observer);
        await Seed(view, team);

        var edited = await SendAsync(new Edit.Command { Id = team.Id, Name = "Renamed only" });

        Assert.Equal(TestData.TeamRoles.Observer, edited.RoleId);
    }

    [Fact]
    public async Task Edit_reports_a_missing_team_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Team>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid(), Name = "Ghost" }));
    }

    // ---- Delete ---------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await SendAsync(new Delete.Command { Id = team.Id });

        await using var db = NewContext();
        Assert.False(await db.Teams.AnyAsync(x => x.Id == team.Id, Ct));
    }

    /// <summary>
    /// Both directions are removed explicitly. A scope naming the deleted team as its target has no
    /// cascade path from it, so it would otherwise be stranded.
    /// </summary>
    [Fact]
    public async Task Delete_removes_scopes_pointing_in_either_direction()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Doomed");
        var other = TestData.Team(view.Id, "Survivor");
        await Seed(
            view, team, other,
            TestData.TeamPermissionScope(team.Id, other.Id),
            TestData.TeamPermissionScope(other.Id, team.Id));

        await SendAsync(new Delete.Command { Id = team.Id });

        await using var db = NewContext();
        Assert.False(await db.TeamPermissionScopes.AnyAsync(Ct));
        Assert.True(await db.Teams.AnyAsync(x => x.Id == other.Id, Ct));
    }

    [Fact]
    public async Task Delete_reports_a_missing_team_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Team>>(
            () => SendAsync(new Delete.Command { Id = Guid.NewGuid() }));
    }

    // ---- SetPrimary -----------------------------------------------------------------------------

    [Fact]
    public async Task SetPrimary_points_the_view_membership_at_the_named_team()
    {
        var view = TestData.View();
        var first = TestData.Team(view.Id, "First");
        var second = TestData.Team(view.Id, "Second");

        var builder = new ClaimsPrincipalBuilder();
        var user = TestData.User(builder.UserId);
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var secondMembership = TestData.TeamMembership(second.Id, user.Id, viewMembership.Id);
        await Seed(
            view, first, second, user, viewMembership,
            TestData.TeamMembership(first.Id, user.Id, viewMembership.Id),
            secondMembership);

        var result = await SendAsync(builder.Build(), new SetPrimary.Command
        {
            UserId = user.Id,
            TeamId = second.Id
        });

        Assert.Equal(second.Id, result.Id);

        await using var db = NewContext();
        Assert.Equal(
            secondMembership.Id,
            (await db.ViewMemberships.SingleAsync(x => x.Id == viewMembership.Id, Ct)).PrimaryTeamMembershipId);
    }

    /// <summary>
    /// A conflict, not a forbidden: the caller may make the change, but the target is not a team they
    /// belong to.
    /// </summary>
    [Fact]
    public async Task SetPrimary_rejects_a_team_the_user_is_not_a_member_of()
    {
        var view = TestData.View();
        var member = TestData.Team(view.Id, "Member of");
        var stranger = TestData.Team(view.Id, "Not a member of");

        var builder = new ClaimsPrincipalBuilder();
        var user = TestData.User(builder.UserId);
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(
            view, member, stranger, user, viewMembership,
            TestData.TeamMembership(member.Id, user.Id, viewMembership.Id));

        await Assert.ThrowsAsync<ConflictException>(() => SendAsync(
            builder.Build(),
            new SetPrimary.Command { UserId = user.Id, TeamId = stranger.Id }));
    }

    /// <summary>
    /// This handler's <c>Authorize</c> asks only whether the subject is the caller, so no permission
    /// grants it.
    /// </summary>
    [Fact]
    public async Task SetPrimary_is_forbidden_for_another_user_even_with_every_system_permission()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        await Seed(view, team, user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(new SetPrimary.Command { UserId = user.Id, TeamId = team.Id }));
    }

    // ---- SendNotification -----------------------------------------------------------------------

    [Fact]
    public async Task SendNotification_persists_it_and_broadcasts_to_the_team_group()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Recipients");
        await Seed(view, team);

        var result = await SendAsync(new SendNotification.Command
        {
            TeamId = team.Id,
            Subject = "Heads up",
            Text = "Something happened"
        });

        Assert.Contains(team.Id.ToString(), result);

        await using var db = NewContext();
        var notification = await db.Notifications.SingleAsync(Ct);
        Assert.Equal(team.Id, notification.ToId);
        Assert.Equal(NotificationType.Team, notification.ToType);

        RootHost.TeamHub.Clients.Received().Group(team.Id.ToString());
    }

    [Fact]
    public async Task SendNotification_rejects_a_notification_with_no_text()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ArgumentException>(
            () => SendAsync(new SendNotification.Command { TeamId = team.Id, Subject = "No body" }));
    }

    [Fact]
    public async Task SendNotification_is_forbidden_without_ManageView_on_the_view()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(user, new SendNotification.Command { TeamId = team.Id, Text = "Nope" }));
    }
}
