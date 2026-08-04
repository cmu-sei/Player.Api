// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.TeamPermissionScopes;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.TeamPermissionScopes;

/// <summary>
/// Covers the <c>TeamPermissionScopes</c> feature's two commands. A scope projects one team's permissions
/// onto another, which is how a user on the granting team acts on the target team's resources.
/// </summary>
public class TeamPermissionScopeRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Add_records_the_scope()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target);

        await SendAsync(new Add.Command { TeamId = team.Id, TargetTeamId = target.Id });

        await using var db = NewContext();
        Assert.True(await db.TeamPermissionScopes.AnyAsync(
            x => x.TeamId == team.Id && x.TargetTeamId == target.Id, Ct));
    }

    /// <summary>
    /// Adding a scope that already exists is not an error, and does not duplicate the row.
    /// </summary>
    [Fact]
    public async Task Add_is_idempotent()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target, TestData.TeamPermissionScope(team.Id, target.Id));

        await SendAsync(new Add.Command { TeamId = team.Id, TargetTeamId = target.Id });

        await using var db = NewContext();
        Assert.Single(await db.TeamPermissionScopes.ToListAsync(Ct));
    }

    /// <summary>
    /// A self-scope would be a no-op that reads as a grant, so it is refused rather than stored.
    /// </summary>
    [Fact]
    public async Task Add_refuses_to_scope_a_team_onto_itself()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ConflictException>(
            () => SendAsync(new Add.Command { TeamId = team.Id, TargetTeamId = team.Id }));
    }

    /// <summary>
    /// Both directions are checked, and the messages distinguish them — a caller needs to know which id
    /// was wrong.
    /// </summary>
    [Fact]
    public async Task Add_reports_a_missing_granting_team_as_not_found()
    {
        var view = TestData.View();
        var target = TestData.Team(view.Id);
        await Seed(view, target);

        var exception = await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new Add.Command { TeamId = Guid.NewGuid(), TargetTeamId = target.Id }));

        Assert.Contains("Granting", exception.Message);
    }

    [Fact]
    public async Task Add_reports_a_missing_target_team_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var exception = await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new Add.Command { TeamId = team.Id, TargetTeamId = Guid.NewGuid() }));

        Assert.Contains("Target", exception.Message);
    }

    /// <summary>
    /// Permissions are scoped within a view. Crossing views would let a view administrator grant into a
    /// view they hold nothing on.
    /// </summary>
    [Fact]
    public async Task Add_refuses_teams_in_different_views()
    {
        var view = TestData.View("First");
        var otherView = TestData.View("Second");
        var team = TestData.Team(view.Id);
        var target = TestData.Team(otherView.Id);
        await Seed(view, otherView, team, target);

        await Assert.ThrowsAsync<ConflictException>(
            () => SendAsync(new Add.Command { TeamId = team.Id, TargetTeamId = target.Id }));
    }

    [Fact]
    public async Task Add_is_allowed_for_a_caller_holding_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();

        await SendAsync(caller, new Add.Command { TeamId = team.Id, TargetTeamId = target.Id });

        await using var db = NewContext();
        Assert.True(await db.TeamPermissionScopes.AnyAsync(Ct));
    }

    [Fact]
    public async Task Add_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Add.Command { TeamId = team.Id, TargetTeamId = target.Id }));
    }

    [Fact]
    public async Task Remove_deletes_the_scope()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target, TestData.TeamPermissionScope(team.Id, target.Id));

        await SendAsync(new Remove.Command { TeamId = team.Id, TargetTeamId = target.Id });

        await using var db = NewContext();
        Assert.False(await db.TeamPermissionScopes.AnyAsync(Ct));
    }

    /// <summary>
    /// Direction matters: the reverse scope is a separate grant and is left in place.
    /// </summary>
    [Fact]
    public async Task Remove_leaves_the_scope_in_the_other_direction_alone()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(
            view, team, target,
            TestData.TeamPermissionScope(team.Id, target.Id),
            TestData.TeamPermissionScope(target.Id, team.Id));

        await SendAsync(new Remove.Command { TeamId = team.Id, TargetTeamId = target.Id });

        await using var db = NewContext();
        Assert.Equal(target.Id, (await db.TeamPermissionScopes.SingleAsync(Ct)).TeamId);
    }

    /// <summary>
    /// A scope that is not there is not an error — the request is already satisfied.
    /// </summary>
    [Fact]
    public async Task Remove_does_nothing_when_the_scope_is_absent()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target);

        await SendAsync(new Remove.Command { TeamId = team.Id, TargetTeamId = target.Id });
    }

    [Fact]
    public async Task Remove_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id, "Granting");
        var target = TestData.Team(view.Id, "Target");
        await Seed(view, team, target, TestData.TeamPermissionScope(team.Id, target.Id));

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Remove.Command { TeamId = team.Id, TargetTeamId = target.Id }));
    }
}
