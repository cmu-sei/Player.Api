// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// Covers <c>AuthorizationService</c> — the seam every MediatR handler's <c>Authorize()</c> calls
/// through, and the claim readers endpoints use to scope their results.
/// </summary>
/// <remarks>
/// Database-backed because the production type takes a <c>PlayerContext</c>. The claim readers do not
/// query it, but constructing the real service with a real context beats passing null and hoping the
/// implementation never starts using it.
/// </remarks>
public class AuthorizationServiceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly Guid ViewId = Guid.NewGuid();
    private static readonly Guid OtherViewId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid OtherTeamId = Guid.NewGuid();

    private IPlayerAuthorizationService ServiceFor(ClaimsPrincipal principal) =>
        AuthorizationHarness.CreatePlayerAuthorizationService(principal, Db);

    // ---- GetSystemPermissions -------------------------------------------------------------------

    [Fact]
    public void GetSystemPermissions_returns_the_raw_claim_values()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ViewViews, SystemPermission.ManageUsers)
            .Build();

        var permissions = ServiceFor(user).GetSystemPermissions();

        Assert.Equal(
            [SystemPermission.ViewViews.ToString(), SystemPermission.ManageUsers.ToString()],
            permissions);
    }

    [Fact]
    public void GetSystemPermissions_includes_values_that_are_not_enum_members()
    {
        // Deliberately unfiltered: this feeds the "what can I do" endpoint, and silently dropping a
        // stale permission there would hide a misconfigured role rather than surface it.
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ViewViews)
            .WithRawSystemPermission("RetiredPermission")
            .Build();

        var permissions = ServiceFor(user).GetSystemPermissions();

        Assert.Contains("RetiredPermission", permissions);
    }

    [Fact]
    public void GetSystemPermissions_ignores_team_permission_claims()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, viewPermissions: [ViewPermission.ManageView])
            .Build();

        Assert.Empty(ServiceFor(user).GetSystemPermissions());
    }

    // ---- GetAuthorizedViewIds / GetTeamPermissions ---------------------------------------------

    [Fact]
    public void GetAuthorizedViewIds_returns_one_entry_per_team_claim()
    {
        // Not distinct: two teams in one view yield that view twice. Pinned as-is because callers
        // treat it as a membership filter, where duplicates are harmless — but a caller that counts
        // views would be wrong.
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId)
            .WithTeam(ViewId, OtherTeamId)
            .WithTeam(OtherViewId, Guid.NewGuid())
            .Build();

        var viewIds = ServiceFor(user).GetAuthorizedViewIds().ToArray();

        Assert.Equal(3, viewIds.Length);
        Assert.Equal(2, viewIds.Count(x => x == ViewId));
        Assert.Contains(OtherViewId, viewIds);
    }

    [Fact]
    public void GetAuthorizedViewIds_is_empty_for_a_user_with_no_team_claims()
    {
        Assert.Empty(ServiceFor(ClaimsPrincipalBuilder.Anonymous()).GetAuthorizedViewIds());
    }

    [Fact]
    public void GetTeamPermissions_parses_every_team_claim()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, isPrimary: true, teamPermissions: [TeamPermission.ManageTeam])
            .WithTeam(OtherViewId, OtherTeamId, viewPermissions: [ViewPermission.ViewView])
            .Build();

        var claims = ServiceFor(user).GetTeamPermissions().ToArray();

        Assert.Equal(2, claims.Length);

        var primary = Assert.Single(claims, x => x.IsPrimary);
        Assert.Equal(TeamId, primary.TeamId);
        Assert.Equal([TeamPermission.ManageTeam], primary.TeamPermissions);
    }

    // ---- GetVisibleTeamIds ----------------------------------------------------------------------

    [Fact]
    public void GetVisibleTeamIds_returns_teams_granting_ViewTeam_or_ManageTeam()
    {
        var manageTeamId = Guid.NewGuid();
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ViewTeam])
            .WithTeam(ViewId, manageTeamId, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        var teamIds = ServiceFor(user).GetVisibleTeamIds(ViewId).ToArray();

        Assert.Equal(2, teamIds.Length);
        Assert.Contains(TeamId, teamIds);
        Assert.Contains(manageTeamId, teamIds);
    }

    [Fact]
    public void GetVisibleTeamIds_excludes_teams_granting_only_EditTeam()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.EditTeam])
            .Build();

        Assert.Empty(ServiceFor(user).GetVisibleTeamIds(ViewId));
    }

    [Fact]
    public void GetVisibleTeamIds_excludes_teams_in_other_views()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(OtherViewId, OtherTeamId, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        Assert.Empty(ServiceFor(user).GetVisibleTeamIds(ViewId));
    }

    [Fact]
    public void GetVisibleTeamIds_counts_effective_permissions_not_just_direct_ones()
    {
        // A team the user can see only because another team's permissions were scoped onto it. This is
        // the whole point of the method — it exists to surface scoped teams.
        var user = new ClaimsPrincipalBuilder()
            .WithScopedTeam(
                ViewId,
                OtherTeamId,
                sourceTeamIds: [TeamId],
                teamPermissions: [TeamPermission.ViewTeam],
                directTeamPermissions: [])
            .Build();

        Assert.Equal([OtherTeamId], ServiceFor(user).GetVisibleTeamIds(ViewId));
    }

    [Fact]
    public void GetVisibleTeamIds_deduplicates_repeated_teams()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ViewTeam])
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        Assert.Equal([TeamId], ServiceFor(user).GetVisibleTeamIds(ViewId));
    }

    // ---- IsCurrentUser -------------------------------------------------------------------------

    [Fact]
    public void IsCurrentUser_matches_the_subject_claim()
    {
        var builder = new ClaimsPrincipalBuilder();
        var service = ServiceFor(builder.Build());

        Assert.True(service.IsCurrentUser(builder.UserId));
        Assert.False(service.IsCurrentUser(Guid.NewGuid()));
    }

    // ---- Authorize -----------------------------------------------------------------------------

    [Fact]
    public async Task Authorize_grants_on_a_system_permission_alone()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ManageViews)
            .Build();

        Assert.True(await ServiceFor(user).Authorize([SystemPermission.ManageViews], Ct));
    }

    [Fact]
    public async Task Authorize_grants_on_a_team_permission_when_the_system_permission_is_absent()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, viewPermissions: [ViewPermission.ManageView])
            .Build();

        var granted = await ServiceFor(user).Authorize(
            [SystemPermission.ManageViews],
            [ViewPermission.ManageView],
            [],
            Ct);

        Assert.True(granted);
    }

    [Fact]
    public async Task Authorize_refuses_when_neither_permission_kind_grants()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, viewPermissions: [ViewPermission.ViewView])
            .Build();

        var granted = await ServiceFor(user).Authorize(
            [SystemPermission.ManageViews],
            [ViewPermission.ManageView],
            [],
            Ct);

        Assert.False(granted);
    }

    /// <summary>
    /// Covers the null-array path at the level an endpoint reaches it: the system-permission-only
    /// <c>Authorize</c> overload passes <see langword="null"/> for both permission arrays, so any user
    /// holding a team claim arrives at <c>HasRequiredPermissions</c> with those nulls. The
    /// handler-level counterparts are in <c>TeamPermissionsHandlerTests</c>.
    /// </summary>
    [Fact]
    public async Task Authorize_throws_rather_than_refusing_for_a_team_member_without_the_system_permission()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ServiceFor(user).Authorize([SystemPermission.ManageViews], Ct));
    }

    [Fact]
    public async Task Authorize_refuses_for_a_user_with_no_permissions_at_all()
    {
        var user = ClaimsPrincipalBuilder.Anonymous();

        Assert.False(await ServiceFor(user).Authorize([SystemPermission.ViewViews], Ct));
    }

    // ---- Authorize<T> with a resource id --------------------------------------------------------

    [Fact]
    public async Task Authorize_resolves_a_team_resource_to_its_view_and_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        Db.Views.Add(view);
        Db.Teams.Add(team);
        await Db.SaveChangesAsync(Ct);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        var granted = await ServiceFor(user).Authorize<TeamEntity>(
            team.Id,
            [SystemPermission.ManageViews],
            [],
            [TeamPermission.ManageTeam],
            Ct);

        Assert.True(granted);
    }

    [Fact]
    public async Task Authorize_refuses_when_the_permission_is_held_on_a_different_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Target");
        var otherTeam = TestData.Team(view.Id, "Other");
        Db.Views.Add(view);
        Db.Teams.AddRange(team, otherTeam);
        await Db.SaveChangesAsync(Ct);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, otherTeam.Id, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        var granted = await ServiceFor(user).Authorize<TeamEntity>(
            team.Id,
            [SystemPermission.ManageViews],
            [],
            [TeamPermission.ManageTeam],
            Ct);

        Assert.False(granted);
    }

    /// <summary>
    /// A resource that does not exist is refused, not reported as missing. Distinguishing the two
    /// would let an unauthorized caller probe for the existence of resources it cannot see.
    /// </summary>
    [Fact]
    public async Task Authorize_refuses_when_the_resource_does_not_exist()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        var granted = await ServiceFor(user).Authorize<TeamEntity>(
            Guid.NewGuid(),
            [SystemPermission.ManageViews],
            [],
            [TeamPermission.ManageTeam],
            Ct);

        Assert.False(granted);
    }

    [Fact]
    public async Task Authorize_grants_on_a_system_permission_without_looking_up_the_resource()
    {
        // The system check runs first and short-circuits, so a nonexistent resource id is irrelevant.
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ManageViews)
            .Build();

        var granted = await ServiceFor(user).Authorize<TeamEntity>(
            Guid.NewGuid(),
            [SystemPermission.ManageViews],
            [],
            [TeamPermission.ManageTeam],
            Ct);

        Assert.True(granted);
    }

    [Fact]
    public async Task Authorize_throws_for_a_resource_type_it_cannot_resolve()
    {
        // A guard against a new entity type silently authorizing everyone: the switch throws rather
        // than falling through to a permissive default.
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            ServiceFor(user).Authorize<UserEntity>(
                Guid.NewGuid(),
                [SystemPermission.ManageViews],
                [],
                [TeamPermission.ManageTeam],
                Ct));
    }
}
