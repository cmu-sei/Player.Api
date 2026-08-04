// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.Features.TeamMemberships;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.TeamMemberships;

/// <summary>
/// Covers the <c>TeamMemberships</c> feature's request handlers. A team membership carries the role a
/// user holds on one team, so these handlers are how a user's team role is read and changed.
/// </summary>
public class TeamMembershipRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Get_returns_the_membership_with_its_names_resolved()
    {
        var view = TestData.View("Exercise");
        var team = TestData.Team(view.Id, "Blue");
        var user = TestData.User(name: "Member");
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(
            team.Id, user.Id, viewMembership.Id, TestData.TeamRoles.Observer);
        await Seed(view, team, user, viewMembership, teamMembership);

        var result = await SendAsync(new Get.Query { Id = teamMembership.Id });

        Assert.Equal("Member", result.UserName);
        Assert.Equal("Blue", result.TeamName);
        Assert.Equal("Observer", result.RoleName);
        Assert.Equal(view.Id, result.ViewId);
    }

    /// <summary>
    /// A membership with no role of its own falls back to the team's role, which the projection reports
    /// as no role name rather than the team's.
    /// </summary>
    [Fact]
    public async Task Get_reports_no_role_name_when_the_membership_has_no_role()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(view, team, user, viewMembership, teamMembership);

        Assert.Null((await SendAsync(new Get.Query { Id = teamMembership.Id })).RoleName);
    }

    [Fact]
    public async Task Get_reports_the_primary_membership_as_primary()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(view, team, user, viewMembership, teamMembership);
        viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
        await Db.SaveChangesAsync(Ct);

        Assert.True((await SendAsync(new Get.Query { Id = teamMembership.Id })).isPrimary);
    }

    [Fact]
    public async Task Get_reports_a_missing_membership_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamMembership>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    /// <summary>
    /// Authorized against the membership's own team, so <c>ManageTeam</c> on that team is enough.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ManageTeam_on_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(view, team, user, viewMembership, teamMembership);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        Assert.Equal(
            teamMembership.Id,
            (await SendAsync(caller, new Get.Query { Id = teamMembership.Id })).Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(view, team, user, viewMembership, teamMembership);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Get.Query { Id = teamMembership.Id }));
    }

    [Fact]
    public async Task GetByUserView_returns_only_that_user_memberships_in_that_view()
    {
        var view = TestData.View();
        var otherView = TestData.View("Other");
        var team = TestData.Team(view.Id, "In view");
        var otherTeam = TestData.Team(otherView.Id, "Elsewhere");
        var user = TestData.User();
        var stranger = TestData.User(name: "Stranger");
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var otherViewMembership = TestData.ViewMembership(otherView.Id, user.Id);
        var strangerMembership = TestData.ViewMembership(view.Id, stranger.Id);
        await Seed(
            view, otherView, team, otherTeam, user, stranger,
            viewMembership, otherViewMembership, strangerMembership,
            TestData.TeamMembership(team.Id, user.Id, viewMembership.Id),
            TestData.TeamMembership(otherTeam.Id, user.Id, otherViewMembership.Id),
            TestData.TeamMembership(team.Id, stranger.Id, strangerMembership.Id));

        var memberships = await SendAsync(new GetByUserView.Query { UserId = user.Id, ViewId = view.Id });

        Assert.Equal("In view", Assert.Single(memberships).TeamName);
    }

    /// <summary>
    /// A user may always read their own memberships, which is how the UI resolves the caller's own teams.
    /// </summary>
    [Fact]
    public async Task GetByUserView_is_allowed_for_the_caller_asking_about_themselves()
    {
        var builder = new ClaimsPrincipalBuilder();
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User(builder.UserId);
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(
            view, team, user, viewMembership,
            TestData.TeamMembership(team.Id, user.Id, viewMembership.Id));

        Assert.Single(await SendAsync(
            builder.Build(),
            new GetByUserView.Query { UserId = user.Id, ViewId = view.Id }));
    }

    [Fact]
    public async Task GetByUserView_reports_a_missing_user_as_not_found()
    {
        var view = TestData.View();
        await Seed(view);

        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Users.User>>(
            () => SendAsync(new GetByUserView.Query { UserId = Guid.NewGuid(), ViewId = view.Id }));
    }

    [Fact]
    public async Task GetByUserView_is_forbidden_for_a_caller_asking_about_someone_else()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user, TestData.ViewMembership(view.Id, user.Id));

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetByUserView.Query { UserId = user.Id, ViewId = view.Id }));
    }

    [Fact]
    public async Task Edit_changes_the_role()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(
            team.Id, user.Id, viewMembership.Id, TestData.TeamRoles.Observer);
        await Seed(view, team, user, viewMembership, teamMembership);

        var edited = await SendAsync(new Edit.Command
        {
            Id = teamMembership.Id,
            RoleId = TestData.TeamRoles.ViewAdmin
        });

        Assert.Equal(TestData.TeamRoles.ViewAdmin, edited.RoleId);
        Assert.Equal("View Admin", edited.RoleName);
    }

    /// <summary>
    /// Clearing the role returns the member to the team's own role.
    /// </summary>
    [Fact]
    public async Task Edit_clears_the_role_when_none_is_given()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(
            team.Id, user.Id, viewMembership.Id, TestData.TeamRoles.Observer);
        await Seed(view, team, user, viewMembership, teamMembership);

        var edited = await SendAsync(new Edit.Command { Id = teamMembership.Id, RoleId = null });

        Assert.Null(edited.RoleId);
    }

    [Fact]
    public async Task Edit_reports_a_missing_membership_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamMembership>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid() }));
    }

    /// <summary>
    /// Team roles are a view-level concern: <c>ManageTeam</c> on the team is not enough, only
    /// <c>ManageView</c> or the system permission.
    /// </summary>
    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ManageTeam()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(view, team, user, viewMembership, teamMembership);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            caller,
            new Edit.Command { Id = teamMembership.Id, RoleId = TestData.TeamRoles.ViewAdmin }));
    }
}
