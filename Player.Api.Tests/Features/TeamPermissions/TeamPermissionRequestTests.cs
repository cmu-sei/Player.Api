// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;
using Player.Api.Features.TeamPermissions;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Features.TeamPermissions;

/// <summary>
/// Covers the <c>TeamPermissions</c> feature's request handlers: the permission rows themselves, their
/// grants to roles and to individual teams, and the claims-only <c>GetMine</c> query.
/// </summary>
public class TeamPermissionRequestTests(DatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ---- Create / Edit / Delete -----------------------------------------------------------------

    [Fact]
    public async Task Create_persists_the_permission()
    {
        var created = await SendAsync(new Create.Command { Name = "Custom", Description = "A custom one" });

        Assert.Equal("Custom", created.Name);

        await using var db = NewContext();
        Assert.True(await db.TeamPermissions.AnyAsync(x => x.Id == created.Id, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_without_ManageRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Create.Command { Name = "Nope" }));
    }

    [Fact]
    public async Task Edit_updates_a_mutable_permission()
    {
        var edited = await SendAsync(new Edit.Command
        {
            Id = TestData.TeamPermissions.UploadViewIsos,
            Name = "UploadViewIsos",
            Description = "Reworded"
        });

        Assert.Equal("Reworded", edited.Description);
    }

    /// <summary>
    /// The immutable flag protects the permissions team authorization resolves by name: renaming one
    /// would silently revoke it from every role and team that grants it.
    /// </summary>
    [Fact]
    public async Task Edit_refuses_an_immutable_permission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(new Edit.Command
        {
            Id = TestData.TeamPermissions.ViewTeam,
            Name = "Renamed"
        }));
    }

    [Fact]
    public async Task Edit_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamPermissionModel>>(
            () => SendAsync(new Edit.Command { Id = Guid.NewGuid(), Name = "Ghost" }));
    }

    [Fact]
    public async Task Delete_removes_a_mutable_permission()
    {
        await SendAsync(new Delete.Command { Id = TestData.TeamPermissions.UploadViewIsos });

        await using var db = NewContext();
        Assert.False(await db.TeamPermissions.AnyAsync(
            x => x.Id == TestData.TeamPermissions.UploadViewIsos, Ct));
    }

    [Fact]
    public async Task Delete_refuses_an_immutable_permission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(new Delete.Command { Id = TestData.TeamPermissions.ViewTeam }));
    }

    [Fact]
    public async Task Delete_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamPermissionModel>>(
            () => SendAsync(new Delete.Command { Id = Guid.NewGuid() }));
    }

    // ---- Get / GetAll ---------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_permission()
    {
        Assert.Equal(
            TeamPermission.ViewTeam.ToString(),
            (await SendAsync(new Get.Query { Id = TestData.TeamPermissions.ViewTeam })).Name);
    }

    [Fact]
    public async Task Get_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamPermissionModel>>(
            () => SendAsync(new Get.Query { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_with_no_permissions()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new Get.Query { Id = TestData.TeamPermissions.ViewTeam }));
    }

    [Fact]
    public async Task GetAll_returns_every_permission()
    {
        var permissions = await SendAsync(new GetAll.Query());

        Assert.Contains(permissions, x => x.Id == TestData.TeamPermissions.ViewTeam);
        Assert.Contains(permissions, x => x.Id == TestData.TeamPermissions.UploadViewIsos);
    }

    /// <summary>
    /// Team administrators need the list to grant permissions, so <c>ManageTeam</c> on any team admits
    /// the caller without a system permission.
    /// </summary>
    [Fact]
    public async Task GetAll_is_allowed_for_a_caller_holding_ManageTeam_on_any_team()
    {
        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(Guid.NewGuid(), Guid.NewGuid(), teamPermissions: [TeamPermission.ManageTeam])
            .Build();

        Assert.NotEmpty(await SendAsync(caller, new GetAll.Query()));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_with_no_permissions()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => SendAsync(ClaimsPrincipalBuilder.Anonymous(), new GetAll.Query()));
    }

    // ---- GetMine ---------------------------------------------------------------------------------

    /// <summary>
    /// Open to any caller and answered from the claims: it reports what the caller holds, so there is
    /// nothing to authorize.
    /// </summary>
    [Fact]
    public async Task GetMine_returns_every_team_claim_when_unfiltered()
    {
        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(Guid.NewGuid(), Guid.NewGuid())
            .WithTeam(Guid.NewGuid(), Guid.NewGuid())
            .Build();

        Assert.Equal(2, (await SendAsync(caller, new GetMine.Query())).Length);
    }

    [Fact]
    public async Task GetMine_returns_nothing_for_a_caller_on_no_teams()
    {
        Assert.Empty(await SendAsync(ClaimsPrincipalBuilder.Anonymous(), new GetMine.Query()));
    }

    [Fact]
    public async Task GetMine_filters_by_view()
    {
        var view = Guid.NewGuid();
        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view, Guid.NewGuid())
            .WithTeam(view, Guid.NewGuid())
            .WithTeam(Guid.NewGuid(), Guid.NewGuid())
            .Build();

        var claims = await SendAsync(caller, new GetMine.Query { ViewId = view });

        Assert.Equal(2, claims.Length);
        Assert.All(claims, x => Assert.Equal(view, x.ViewId));
    }

    /// <summary>
    /// A view filter wins: the team filter is only consulted when no view was given.
    /// </summary>
    [Fact]
    public async Task GetMine_ignores_the_team_when_a_view_is_also_given()
    {
        var view = Guid.NewGuid();
        var team = Guid.NewGuid();
        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view, team)
            .WithTeam(view, Guid.NewGuid())
            .Build();

        Assert.Equal(2, (await SendAsync(caller, new GetMine.Query { ViewId = view, TeamId = team })).Length);
    }

    [Fact]
    public async Task GetMine_filters_by_team()
    {
        var view = Guid.NewGuid();
        var team = Guid.NewGuid();
        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view, team)
            .WithTeam(view, Guid.NewGuid())
            .Build();

        var claims = await SendAsync(caller, new GetMine.Query { TeamId = team });

        Assert.Equal(team, Assert.Single(claims).TeamId);
    }

    /// <summary>
    /// The view comes off the matching claim, so no database read is needed.
    /// </summary>
    [Fact]
    public async Task GetMine_widens_to_the_view_of_a_team_the_caller_is_on()
    {
        var view = Guid.NewGuid();
        var team = Guid.NewGuid();
        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view, team)
            .WithTeam(view, Guid.NewGuid())
            .WithTeam(Guid.NewGuid(), Guid.NewGuid())
            .Build();

        var claims = await SendAsync(
            caller,
            new GetMine.Query { TeamId = team, IncludeAllViewTeams = true });

        Assert.Equal(2, claims.Length);
        Assert.All(claims, x => Assert.Equal(view, x.ViewId));
    }

    /// <summary>
    /// With no claim for the requested team the view has to be looked up, which is how a caller who is
    /// on one team in a view can discover their permissions on the others.
    /// </summary>
    [Fact]
    public async Task GetMine_looks_up_the_view_of_a_team_the_caller_is_not_on()
    {
        var view = TestData.View();
        var known = TestData.Team(view.Id, "Known");
        var unknown = TestData.Team(view.Id, "Unknown");
        await Seed(view, known, unknown);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, known.Id)
            .WithTeam(Guid.NewGuid(), Guid.NewGuid())
            .Build();

        var claims = await SendAsync(
            caller,
            new GetMine.Query { TeamId = unknown.Id, IncludeAllViewTeams = true });

        Assert.Equal(known.Id, Assert.Single(claims).TeamId);
    }

    // ---- AddToRole / RemoveFromRole -------------------------------------------------------------

    [Fact]
    public async Task AddToRole_grants_the_permission_to_the_role()
    {
        await SendAsync(new AddToRole.Command
        {
            RoleId = TestData.TeamRoles.Observer,
            TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
        });

        await using var db = NewContext();
        Assert.True(await db.TeamRolePermissions.AnyAsync(
            x => x.RoleId == TestData.TeamRoles.Observer &&
                 x.PermissionId == TestData.TeamPermissions.UploadViewIsos,
            Ct));
    }

    [Fact]
    public async Task AddToRole_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.TeamRoles.TeamRole>>(
            () => SendAsync(new AddToRole.Command
            {
                RoleId = Guid.NewGuid(),
                TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
            }));
    }

    [Fact]
    public async Task AddToRole_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamPermissionModel>>(
            () => SendAsync(new AddToRole.Command
            {
                RoleId = TestData.TeamRoles.Observer,
                TeamPermissionId = Guid.NewGuid()
            }));
    }

    [Fact]
    public async Task AddToRole_is_forbidden_without_ManageRoles()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new AddToRole.Command
            {
                RoleId = TestData.TeamRoles.Observer,
                TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
            }));
    }

    [Fact]
    public async Task RemoveFromRole_revokes_the_permission()
    {
        await Seed(new TeamRolePermissionEntity(
            TestData.TeamRoles.Observer, TestData.TeamPermissions.UploadViewIsos));

        await SendAsync(new RemoveFromRole.Command
        {
            RoleId = TestData.TeamRoles.Observer,
            TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
        });

        await using var db = NewContext();
        Assert.False(await db.TeamRolePermissions.AnyAsync(
            x => x.RoleId == TestData.TeamRoles.Observer &&
                 x.PermissionId == TestData.TeamPermissions.UploadViewIsos,
            Ct));
    }

    /// <summary>
    /// A grant that is not there is not an error — the request is already satisfied.
    /// </summary>
    [Fact]
    public async Task RemoveFromRole_does_nothing_when_the_role_does_not_hold_the_permission()
    {
        await SendAsync(new RemoveFromRole.Command
        {
            RoleId = TestData.TeamRoles.Observer,
            TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
        });
    }

    [Fact]
    public async Task RemoveFromRole_reports_a_missing_role_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.TeamRoles.TeamRole>>(
            () => SendAsync(new RemoveFromRole.Command
            {
                RoleId = Guid.NewGuid(),
                TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
            }));
    }

    [Fact]
    public async Task RemoveFromRole_reports_a_missing_permission_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<TeamPermissionModel>>(
            () => SendAsync(new RemoveFromRole.Command
            {
                RoleId = TestData.TeamRoles.Observer,
                TeamPermissionId = Guid.NewGuid()
            }));
    }

    // ---- AddToTeam / RemoveFromTeam -------------------------------------------------------------

    /// <summary>
    /// A grant to one team, outside any role — how a single team gets a permission its role does not
    /// carry.
    /// </summary>
    [Fact]
    public async Task AddToTeam_grants_the_permission_to_the_team()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await SendAsync(new AddToTeam.Command
        {
            TeamId = team.Id,
            TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
        });

        await using var db = NewContext();
        Assert.True(await db.TeamPermissionAssignments.AnyAsync(
            x => x.TeamId == team.Id && x.PermissionId == TestData.TeamPermissions.UploadViewIsos,
            Ct));
    }

    [Fact]
    public async Task AddToTeam_reports_a_missing_team_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new AddToTeam.Command
            {
                TeamId = Guid.NewGuid(),
                TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
            }));
    }

    [Fact]
    public async Task AddToTeam_reports_a_missing_permission_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<EntityNotFoundException<TeamPermissionModel>>(
            () => SendAsync(new AddToTeam.Command
            {
                TeamId = team.Id,
                TeamPermissionId = Guid.NewGuid()
            }));
    }

    /// <summary>
    /// Scoped to the team, so <c>ManageView</c> on the containing view is enough — a system permission
    /// is not required.
    /// </summary>
    [Fact]
    public async Task AddToTeam_is_allowed_for_a_caller_holding_ManageView()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        var caller = new ClaimsPrincipalBuilder()
            .WithTeam(view.Id, team.Id, viewPermissions: [ViewPermission.ManageView])
            .Build();

        await SendAsync(caller, new AddToTeam.Command
        {
            TeamId = team.Id,
            TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
        });

        await using var db = NewContext();
        Assert.True(await db.TeamPermissionAssignments.AnyAsync(x => x.TeamId == team.Id, Ct));
    }

    [Fact]
    public async Task AddToTeam_is_forbidden_for_a_caller_with_no_permissions()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(
            ClaimsPrincipalBuilder.Anonymous(),
            new AddToTeam.Command
            {
                TeamId = team.Id,
                TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
            }));
    }

    [Fact]
    public async Task RemoveFromTeam_revokes_the_permission()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(
            view, team,
            new TeamPermissionAssignmentEntity(team.Id, TestData.TeamPermissions.UploadViewIsos));

        await SendAsync(new RemoveFromTeam.Command
        {
            TeamId = team.Id,
            TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
        });

        await using var db = NewContext();
        Assert.False(await db.TeamPermissionAssignments.AnyAsync(x => x.TeamId == team.Id, Ct));
    }

    [Fact]
    public async Task RemoveFromTeam_does_nothing_when_the_team_does_not_hold_the_permission()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await SendAsync(new RemoveFromTeam.Command
        {
            TeamId = team.Id,
            TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
        });
    }

    [Fact]
    public async Task RemoveFromTeam_reports_a_missing_team_as_not_found()
    {
        await Assert.ThrowsAsync<EntityNotFoundException<Player.Api.Features.Teams.Team>>(
            () => SendAsync(new RemoveFromTeam.Command
            {
                TeamId = Guid.NewGuid(),
                TeamPermissionId = TestData.TeamPermissions.UploadViewIsos
            }));
    }

    [Fact]
    public async Task RemoveFromTeam_reports_a_missing_permission_as_not_found()
    {
        var view = TestData.View();
        var team = TestData.Team(view.Id);
        await Seed(view, team);

        await Assert.ThrowsAsync<EntityNotFoundException<TeamPermissionModel>>(
            () => SendAsync(new RemoveFromTeam.Command
            {
                TeamId = team.Id,
                TeamPermissionId = Guid.NewGuid()
            }));
    }
}
