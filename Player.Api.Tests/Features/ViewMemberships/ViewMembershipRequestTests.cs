// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.Features.ViewMemberships;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.ViewMemberships;

/// <summary>
/// Covers the <c>ViewMemberships</c> feature's two query handlers. A view membership records that a user
/// is in a view and which of their teams there is primary.
/// </summary>
public class ViewMembershipRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    /// <summary>
    /// Seeds a user into <paramref name="view"/> on a team, with that team as their primary — the shape
    /// every write path leaves behind, and the one the projection requires.
    /// </summary>
    private async Task<ViewMembershipEntity> SeedMember(
        ViewEntity view,
        UserEntity user,
        string teamName = "Blue")
    {
        var team = TestData.Team(view.Id, teamName);
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(team, viewMembership, teamMembership);

        viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
        await Db.SaveChangesAsync(Ct);

        return viewMembership;
    }

    [Fact]
    public async Task Get_returns_the_membership_with_its_primary_team_resolved()
    {
        var view = TestData.View("Exercise");
        var team = TestData.Team(view.Id, "Blue");
        var user = TestData.User(name: "Member");
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        var teamMembership = TestData.TeamMembership(team.Id, user.Id, viewMembership.Id);
        await Seed(view, team, user, viewMembership, teamMembership);
        viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
        await Db.SaveChangesAsync(Ct);

        var result = await SendAsync(new Get.Query { Id = viewMembership.Id });

        Assert.Equal("Member", result.UserName);
        Assert.Equal("Exercise", result.ViewName);
        Assert.Equal(team.Id, result.PrimaryTeamId);
        Assert.Equal("Blue", result.PrimaryTeamName);
    }

    [Fact]
    public async Task Get_reports_a_missing_membership_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<ViewMembership>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    /// <summary>
    /// Characterizes current behaviour. <c>ViewMembership.PrimaryTeamId</c> is a non-nullable
    /// <see cref="Guid"/> projected from the primary team membership, so a membership whose primary is
    /// null cannot be read at all. Every write path clears the primary only between two saves, but a
    /// request that fails in that window leaves a row no later read can return.
    /// </summary>
    [Fact]
    public async Task Get_throws_for_a_membership_with_no_primary_team()
    {
        var view = TestData.View();
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(view, user, viewMembership);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(new Get.Query { Id = viewMembership.Id }));
    }

    /// <summary>
    /// Authorized against the containing view, so <c>ViewView</c> there is enough.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ViewView()
    {
        var view = TestData.View();
        var user = TestData.User();
        await Seed(view, user);
        var viewMembership = await SeedMember(view, user);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, Guid.NewGuid(), viewPermissions: [ViewPermission.ViewView])
            .Build();

        Assert.Equal(
            viewMembership.Id,
            (await SendAsync(caller, new Get.Query { Id = viewMembership.Id })).Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(view, user, viewMembership);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Get.Query { Id = viewMembership.Id }));
    }

    [Fact]
    public async Task GetByUser_returns_only_that_user_memberships()
    {
        var view = TestData.View("First");
        var otherView = TestData.View("Second");
        var user = TestData.User();
        var stranger = TestData.User(name: "Stranger");
        await Seed(view, otherView, user, stranger);

        await SeedMember(view, user);
        await SeedMember(otherView, user);
        await SeedMember(view, stranger);

        var memberships = await SendAsync(new GetByUser.Query { UserId = user.Id });

        Assert.Equal(2, memberships.Length);
        Assert.All(memberships, x => Assert.Equal(user.Id, x.UserId));
    }

    /// <summary>
    /// A user may always read their own memberships, which is how the UI lists the views a caller can
    /// enter.
    /// </summary>
    [Fact]
    public async Task GetByUser_is_allowed_for_the_caller_asking_about_themselves()
    {
        var builder = new ClaimsPrincipalBuilder();
        var view = TestData.View();
        var user = TestData.User(builder.UserId);
        await Seed(view, user);
        await SeedMember(view, user);

        Assert.Single(await SendAsync(builder.Build(), new GetByUser.Query { UserId = user.Id }));
    }

    [Fact]
    public async Task GetByUser_reports_a_missing_user_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Users.User>>(
            () => SendAsync(new GetByUser.Query { UserId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task GetByUser_is_forbidden_for_a_caller_asking_about_someone_else()
    {
        var user = TestData.User();
        await Seed(user);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new GetByUser.Query { UserId = user.Id }));
    }
}
