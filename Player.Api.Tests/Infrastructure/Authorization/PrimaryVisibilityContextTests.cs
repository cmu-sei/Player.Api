// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// Covers <c>AuthorizationService.GetPrimaryVisibilityContext</c>, the newest and most intricate part
/// of the scoped-team-permissions feature, and the only claim reader that queries the database.
/// </summary>
/// <remarks>
/// The method answers "within this view, which teams can the caller's primary team see?" Two things
/// make it easy to get wrong: it reads <em>direct</em> permissions for the all-teams decision but
/// <em>effective</em> ones elsewhere, and its all-teams branch has to include teams the caller holds
/// no claim on — which is why this test class needs real <c>Teams</c> rows.
/// </remarks>
public class PrimaryVisibilityContextTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private IPlayerAuthorizationService ServiceFor(ClaimsPrincipal principal) =>
        AuthorizationHarness.CreatePlayerAuthorizationService(principal, Db);

    /// <summary>
    /// Seeds a view with three teams and returns them. The third is never claimed by the caller, so
    /// it only appears when the all-teams branch is taken.
    /// </summary>
    private async Task<(ViewEntity View, TeamEntity Primary, TeamEntity Second, TeamEntity Unclaimed)> SeedViewAsync()
    {
        var view = TestData.View();
        var primary = TestData.Team(view.Id, "Primary");
        var second = TestData.Team(view.Id, "Second");
        var unclaimed = TestData.Team(view.Id, "Unclaimed");

        Db.Views.Add(view);
        Db.Teams.AddRange(primary, second, unclaimed);
        await Db.SaveChangesAsync(Ct);

        return (view, primary, second, unclaimed);
    }

    [Fact]
    public async Task Returns_empty_when_the_user_has_no_claim_for_the_view()
    {
        var (view, _, _, _) = await SeedViewAsync();
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(Guid.NewGuid(), Guid.NewGuid(), isPrimary: true, viewPermissions: [ViewPermission.ManageView])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.Same(PrimaryVisibilityContext.Empty, context);
    }

    [Fact]
    public async Task Returns_empty_when_no_claim_for_the_view_is_primary()
    {
        // A user scoped onto teams in a view they are not a member of: claims exist, but none is
        // primary, so there is no vantage point to compute visibility from.
        var (view, primary, _, _) = await SeedViewAsync();
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, primary.Id, isPrimary: false, viewPermissions: [ViewPermission.ManageView])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.Same(PrimaryVisibilityContext.Empty, context);
        Assert.Null(context.PrimaryTeamId);
        Assert.False(context.CanViewAllTeams);
        Assert.Empty(context.TeamIds);
    }

    [Theory]
    [InlineData(ViewPermission.ViewView)]
    [InlineData(ViewPermission.ManageView)]
    public async Task Sees_every_team_in_the_view_with_a_direct_view_permission(ViewPermission permission)
    {
        var (view, primary, second, unclaimed) = await SeedViewAsync();
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, primary.Id, isPrimary: true, viewPermissions: [permission])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.Equal(primary.Id, context.PrimaryTeamId);
        Assert.True(context.CanViewAllTeams);
        // Includes the unclaimed team — this branch reads the database, not the claims.
        Assert.Equal(
            new HashSet<Guid> { primary.Id, second.Id, unclaimed.Id },
            context.TeamIds.ToHashSet());
    }

    [Fact]
    public async Task Does_not_see_teams_from_other_views()
    {
        var (view, primary, second, unclaimed) = await SeedViewAsync();

        var otherView = TestData.View("Other View");
        var otherTeam = TestData.Team(otherView.Id, "Other Team");
        Db.Views.Add(otherView);
        Db.Teams.Add(otherTeam);
        await Db.SaveChangesAsync(Ct);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, primary.Id, isPrimary: true, viewPermissions: [ViewPermission.ManageView])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.DoesNotContain(otherTeam.Id, context.TeamIds);
        Assert.Equal(3, context.TeamIds.Count);
        Assert.Contains(second.Id, context.TeamIds);
        Assert.Contains(unclaimed.Id, context.TeamIds);
    }

    /// <summary>
    /// The distinction that makes this method worth testing: an <em>effective</em> view permission
    /// arriving through a scope must not widen visibility to every team in the view. Only a permission
    /// the primary team holds directly does that.
    /// </summary>
    [Fact]
    public async Task An_effective_but_not_direct_view_permission_does_not_grant_all_teams()
    {
        var (view, primary, _, unclaimed) = await SeedViewAsync();
        var user = new ClaimsPrincipalBuilder()
            .WithScopedTeam(
                view.Id,
                primary.Id,
                sourceTeamIds: [],
                isPrimary: true,
                viewPermissions: [ViewPermission.ManageView],
                directViewPermissions: [])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.False(context.CanViewAllTeams);
        Assert.Equal([primary.Id], context.TeamIds);
        Assert.DoesNotContain(unclaimed.Id, context.TeamIds);
    }

    [Theory]
    [InlineData(TeamPermission.ViewTeam)]
    [InlineData(TeamPermission.ManageTeam)]
    public async Task Sees_teams_scoped_onto_the_primary_team_with_a_direct_team_permission(
        TeamPermission permission)
    {
        var (view, primary, second, unclaimed) = await SeedViewAsync();
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, primary.Id, isPrimary: true, teamPermissions: [permission])
            // `second` is visible because its claim names the primary team as the source of the grant.
            .WithScopedTeam(view.Id, second.Id, sourceTeamIds: [primary.Id])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.False(context.CanViewAllTeams);
        Assert.Equal(new HashSet<Guid> { primary.Id, second.Id }, context.TeamIds.ToHashSet());
        Assert.DoesNotContain(unclaimed.Id, context.TeamIds);
    }

    [Fact]
    public async Task Ignores_scoped_teams_sourced_from_a_different_team()
    {
        var (view, primary, second, _) = await SeedViewAsync();
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, primary.Id, isPrimary: true, teamPermissions: [TeamPermission.ManageTeam])
            .WithScopedTeam(view.Id, second.Id, sourceTeamIds: [Guid.NewGuid()])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.Equal([primary.Id], context.TeamIds);
    }

    [Fact]
    public async Task Sees_only_the_primary_team_without_a_direct_team_or_view_permission()
    {
        var (view, primary, second, _) = await SeedViewAsync();
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, primary.Id, isPrimary: true, teamPermissions: [TeamPermission.EditTeam])
            .WithScopedTeam(view.Id, second.Id, sourceTeamIds: [primary.Id])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.False(context.CanViewAllTeams);
        Assert.Equal([primary.Id], context.TeamIds);
    }

    /// <summary>
    /// A claim serialized before <c>SourceTeamIds</c> existed deserializes with the property absent.
    /// The implementation guards with <c>?? false</c>; this pins that guard.
    /// </summary>
    [Fact]
    public async Task Tolerates_a_claim_with_null_source_team_ids()
    {
        var (view, primary, second, _) = await SeedViewAsync();
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, primary.Id, isPrimary: true, teamPermissions: [TeamPermission.ManageTeam])
            .WithTeamClaim(new TeamPermissionsClaim
            {
                ViewId = view.Id,
                TeamId = second.Id,
                SourceTeamIds = null
            })
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.Equal([primary.Id], context.TeamIds);
    }

    [Fact]
    public async Task Ignores_claims_belonging_to_other_views_when_resolving_the_primary_team()
    {
        // A user is primary in more than one view at once; the wrong view's primary team must not leak.
        var (view, primary, _, _) = await SeedViewAsync();
        var otherView = TestData.View("Other View");
        var otherPrimary = TestData.Team(otherView.Id, "Other Primary");
        Db.Views.Add(otherView);
        Db.Teams.Add(otherPrimary);
        await Db.SaveChangesAsync(Ct);

        var user = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, primary.Id, isPrimary: true, teamPermissions: [TeamPermission.ViewTeam])
            .WithTeam(otherView.Id, otherPrimary.Id, isPrimary: true, viewPermissions: [ViewPermission.ManageView])
            .Build();

        var context = await ServiceFor(user).GetPrimaryVisibilityContext(view.Id, Ct);

        Assert.Equal(primary.Id, context.PrimaryTeamId);
        Assert.False(context.CanViewAllTeams);
    }
}
