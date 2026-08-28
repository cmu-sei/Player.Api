// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Player.Api.Data.Data.Models;
using Player.Api.Features.ViewMemberships;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.ViewMemberships;

/// <summary>
/// Covers the <c>ViewMemberships</c> feature's two read routes over HTTP. A view membership records that
/// a user is in a view and which of their teams there is primary.
/// </summary>
public class ViewMembershipRequestTests(DatabaseFixture fixture, PlayerAppFactory factory)
    : ApiTestBase(fixture, factory)
{
    // ---- Get ------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_membership_with_its_primary_team_resolved()
    {
        var view = TestData.View("Exercise");
        var team = TestData.Team(view.Id, "Blue");
        await Seed(view, team);

        var member = await Actor().WithName("Member").OnTeam(team).SeedAsync();

        var got = await ReadAsync<ViewMembership>(await RootClient.GetAsync(
            $"api/view-memberships/{member.Membership.ViewMembershipId}", Ct));

        Assert.Equal("Member", got.UserName);
        Assert.Equal("Exercise", got.ViewName);
        Assert.Equal(team.Id, got.PrimaryTeamId);
        Assert.Equal("Blue", got.PrimaryTeamName);
    }

    [Fact]
    public async Task Get_reports_a_missing_membership_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/view-memberships/{Guid.NewGuid()}", Ct));
    }

    /// <summary>
    /// Characterizes current behaviour, which is wrong: a membership whose primary team is null cannot be
    /// read at all, and the caller is told a server error rather than anything about the row.
    /// </summary>
    /// <remarks>
    /// <c>PrimaryTeamId</c> is a non-nullable <see cref="Guid"/> projected from the primary team
    /// membership (<c>MappingProfile.cs:15</c>), so a null primary materializes as a failed cast.
    /// <c>Users/RemoveFromTeam.cs:95</c> clears the primary and deletes the row in two saves, so a
    /// request failing between them leaves a row no later read can return. Turns red when the projection
    /// tolerates a null primary.
    /// </remarks>
    [Fact]
    public async Task Get_fails_for_a_membership_with_no_primary_team()
    {
        var view = TestData.View();
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(view, user, viewMembership);

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await RootClient.GetAsync($"api/view-memberships/{viewMembership.Id}", Ct));

        Assert.Equal("Nullable object must have a value.", problem.Detail);
    }

    /// <summary>
    /// Authorized against the containing view, so <c>ViewView</c> there is enough — and it reads another
    /// user's membership, so nothing about the caller's identity is doing the work.
    /// </summary>
    [Fact]
    public async Task Get_is_allowed_for_a_caller_holding_ViewView()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var member = await Actor().WithName("Member").OnTeam(team).SeedAsync();
        var caller = await Actor().OnTeam(team, viewPermissions: [ViewPermission.ViewView]).SeedAsync();

        var got = await ReadAsync<ViewMembership>(await Client(caller).GetAsync(
            $"api/view-memberships/{member.Membership.ViewMembershipId}", Ct));

        Assert.Equal(member.Membership.ViewMembershipId, got.Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var user = TestData.User();
        var viewMembership = TestData.ViewMembership(view.Id, user.Id);
        await Seed(view, user, viewMembership);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/view-memberships/{viewMembership.Id}", Ct));
    }

    // ---- GetByUser ------------------------------------------------------------------------------

    [Fact]
    public async Task GetByUser_returns_only_that_user_memberships()
    {
        var view = TestData.View("First");
        var otherView = TestData.View("Second");
        var team = TestData.Team(view.Id, "Blue");
        var otherTeam = TestData.Team(otherView.Id, "Blue");
        await Seed(view, otherView, team, otherTeam);

        var user = await Actor().OnTeam(team).OnTeam(otherTeam).SeedAsync();
        await Actor().WithName("Stranger").OnTeam(team).SeedAsync();

        var memberships = await ReadAsync<ViewMembership[]>(
            await RootClient.GetAsync($"api/users/{user.Id}/view-memberships", Ct));

        Assert.Equal(2, memberships.Length);
        Assert.All(memberships, x => Assert.Equal(user.Id, x.UserId));
    }

    /// <summary>
    /// A user may always read their own memberships, which is how the UI lists the views a caller can
    /// enter. The team's role grants nothing, so no permission is involved.
    /// </summary>
    [Fact]
    public async Task GetByUser_is_allowed_for_the_caller_asking_about_themselves()
    {
        var view = TestData.View();
        var role = TestData.TeamRole();
        var team = TestData.Team(view.Id, "Team", role.Id);
        await Seed(view, role, team);

        var actor = await Actor().OnTeam(team).SeedAsync();

        Assert.Single(await ReadAsync<ViewMembership[]>(
            await Client(actor).GetAsync($"api/users/{actor.Id}/view-memberships", Ct)));
    }

    [Fact]
    public async Task GetByUser_reports_a_missing_user_as_not_found()
    {
        await AssertProblem(
            HttpStatusCode.NotFound,
            await RootClient.GetAsync($"api/users/{Guid.NewGuid()}/view-memberships", Ct));
    }

    [Fact]
    public async Task GetByUser_is_forbidden_for_a_caller_asking_about_someone_else()
    {
        var user = TestData.User();
        await Seed(user);

        var actor = await Actor().SeedAsync();

        await AssertProblem(
            HttpStatusCode.Forbidden,
            await Client(actor).GetAsync($"api/users/{user.Id}/view-memberships", Ct));
    }

    /// <summary>
    /// The same defect as <c>Get_fails_for_a_membership_with_no_primary_team</c>, and the worse half of
    /// it: one such row fails the whole list, so the caller can read none of their memberships.
    /// </summary>
    /// <remarks>Turns red when the projection tolerates a null primary.</remarks>
    [Fact]
    public async Task GetByUser_fails_when_one_of_the_memberships_has_no_primary_team()
    {
        var view = TestData.View("Readable");
        var broken = TestData.View("No primary team");
        var team = TestData.Team(view.Id);
        await Seed(view, broken, team);

        var actor = await Actor().OnTeam(team).SeedAsync();
        await Seed(TestData.ViewMembership(broken.Id, actor.Id));

        var problem = await AssertProblem(
            HttpStatusCode.InternalServerError,
            await Client(actor).GetAsync($"api/users/{actor.Id}/view-memberships", Ct));

        Assert.Equal("Nullable object must have a value.", problem.Detail);
    }
}
