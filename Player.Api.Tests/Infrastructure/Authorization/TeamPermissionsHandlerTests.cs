// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// Covers <see cref="TeamPermissionsHandler"/>, which decides every view- and team-scoped
/// authorization outcome in the application.
/// </summary>
public class TeamPermissionsHandlerTests
{
    private static readonly Guid ViewId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid OtherViewId = Guid.NewGuid();
    private static readonly Guid OtherTeamId = Guid.NewGuid();

    private readonly TeamPermissionsHandler _handler = new();

    [Fact]
    public async Task Fails_when_there_is_no_user()
    {
        var context = await AuthorizationHarness.HandleAsync(_handler, new TeamPermissionRequirement(), user: null);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_when_the_user_has_no_team_claims()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithSystemPermissions(SystemPermission.ViewViews)
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.ViewView],
            RequiredTeamPermissions: [TeamPermission.ViewTeam]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        // Not "failed" — this handler declines rather than vetoing, leaving the outcome to whichever
        // other handler can satisfy the requirement.
        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task Succeeds_when_the_named_team_grants_a_required_team_permission()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.EditTeam])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredTeamPermissions: [TeamPermission.EditTeam],
            TeamId: TeamId);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_when_the_permission_is_held_on_a_different_team()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, OtherTeamId, teamPermissions: [TeamPermission.EditTeam])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredTeamPermissions: [TeamPermission.EditTeam],
            TeamId: TeamId);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_when_the_named_team_grants_only_an_unrequired_permission()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ViewTeam])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredTeamPermissions: [TeamPermission.ManageTeam],
            TeamId: TeamId);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_the_named_view_grants_a_required_view_permission()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, viewPermissions: [ViewPermission.ManageView])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.ManageView],
            ViewId: ViewId);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_when_the_view_permission_is_held_on_a_different_view()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(OtherViewId, OtherTeamId, viewPermissions: [ViewPermission.ManageView])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.ManageView],
            ViewId: ViewId);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_on_the_view_when_a_second_team_in_that_view_grants_the_permission()
    {
        // The view branch pools permissions across every team the user holds in the view, so a
        // permission from any one of them is enough.
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, viewPermissions: [ViewPermission.ViewView])
            .WithTeam(ViewId, OtherTeamId, viewPermissions: [ViewPermission.ManageView])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.ManageView],
            ViewId: ViewId);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_on_the_view_when_the_team_branch_declined()
    {
        // Both ids are set and the team has no team-level grant, so success must come from the view
        // branch — proving the team branch falls through instead of short-circuiting to a refusal.
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, viewPermissions: [ViewPermission.ManageView])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.ManageView],
            RequiredTeamPermissions: [TeamPermission.ManageTeam],
            ViewId: ViewId,
            TeamId: TeamId);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_from_any_view_when_no_view_or_team_is_named()
    {
        // "Can this user do X anywhere?" — the shape used by list endpoints.
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(OtherViewId, OtherTeamId, viewPermissions: [ViewPermission.EditView])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.EditView],
            RequiredTeamPermissions: [TeamPermission.ManageTeam]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_from_any_team_when_no_view_or_team_is_named()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(OtherViewId, OtherTeamId, teamPermissions: [TeamPermission.ManageTeam])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.EditView],
            RequiredTeamPermissions: [TeamPermission.ManageTeam]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_succeed_when_no_claim_grants_any_required_permission()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, viewPermissions: [ViewPermission.ViewView], teamPermissions: [TeamPermission.ViewTeam])
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.ManageView],
            RequiredTeamPermissions: [TeamPermission.ManageTeam]);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Ignores_permission_values_that_are_not_known_enum_members()
    {
        // A permission removed from the enum but still present in a role in the database. It must be
        // ignored, not crash the request.
        var user = new ClaimsPrincipalBuilder()
            .WithTeamClaim(new TeamPermissionsClaim
            {
                ViewId = ViewId,
                TeamId = TeamId,
                PermissionValues = ["NotARealPermission", TeamPermission.ManageTeam.ToString()],
                DirectPermissionValues = ["NotARealPermission"]
            })
            .Build();
        var requirement = new TeamPermissionRequirement(
            RequiredTeamPermissions: [TeamPermission.ManageTeam],
            TeamId: TeamId);

        var context = await AuthorizationHarness.HandleAsync(_handler, requirement, user);

        Assert.True(context.HasSucceeded);
    }

    // ---- Null permission arrays -----------------------------------------------------------------
    //
    // TeamPermissionRequirement declares every permission array as optional with a null default, and
    // AuthorizationService takes it up on that — its system-permission-only overload constructs the
    // requirement with both arrays null. A user who holds any team claim but lacks the required system
    // permission then reaches HasRequiredPermissions with a null array, where Enumerable.Any throws.

    [Fact]
    public async Task Throws_rather_than_declining_when_no_permissions_are_required()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AuthorizationHarness.HandleAsync(_handler, new TeamPermissionRequirement(), user));
    }

    [Fact]
    public async Task Throws_rather_than_declining_when_only_team_permissions_are_required()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        // RequiredViewPermissions is left null, which is the array that throws.
        var requirement = new TeamPermissionRequirement(
            RequiredTeamPermissions: [TeamPermission.ManageTeam],
            ViewId: ViewId);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AuthorizationHarness.HandleAsync(_handler, requirement, user));
    }

    [Fact]
    public async Task Throws_rather_than_declining_when_only_view_permissions_are_required()
    {
        var user = new ClaimsPrincipalBuilder()
            .WithTeam(ViewId, TeamId, teamPermissions: [TeamPermission.ViewTeam])
            .Build();

        // The mirror image: RequiredTeamPermissions is the null one here.
        var requirement = new TeamPermissionRequirement(
            RequiredViewPermissions: [ViewPermission.ManageView],
            TeamId: TeamId);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AuthorizationHarness.HandleAsync(_handler, requirement, user));
    }
}
