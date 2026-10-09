// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Player.Api.Data.Data.Models;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Migrations;

/// <summary>
/// <c>Add_Vm_And_Map_Permissions</c> run against a deployment's existing data. vm.api stopped reading
/// EditTeam, EditView, ViewTeam, ViewView, ManageTeam, ManageView, ViewViews, ManageViews and EditViews as
/// Vm and Map access, and the migration deletes EditTeam and EditView outright. Each test starts one
/// migration earlier, seeds the roles a real deployment might hold, upgrades, and checks that the same
/// people keep the same access.
/// </summary>
/// <remarks>
/// Seeding and reading go through raw SQL: before the upgrade the schema is that of the earlier
/// migration, which the context's model of today does not describe.
/// </remarks>
public class VmAndMapPermissionsUpgradeTests(DatabaseFixture fixture)
{
    private const string BeforeVmAndMapPermissions = "20260922155600_Add_User_Identity_Attributes";

    private static readonly Guid ObserverRoleId = new("c875dcce-2488-4e73-8585-8375b4730151");
    private static readonly Guid ViewMemberRoleId = new("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(new[] { "ViewTeam", "EditTeam" },
                new[] { "ControlTeamVms", "ViewTeam", "ViewTeamMaps", "ViewTeamVms" })]
    [InlineData(new[] { "ViewView", "EditView" },
                new[] { "ControlViewVms", "ViewView", "ViewViewMaps", "ViewViewVms" })]
    [InlineData(new[] { "ManageTeam" },
                new[] { "ManageTeam", "ManageTeamMaps", "ViewTeamVms" })]
    [InlineData(new[] { "ManageView" },
                new[] { "ManageView", "ManageViewMaps", "ViewViewVms" })]
    [InlineData(new[] { "UploadVmFiles" },
                new[] { "UploadVmFiles" })]
    public async Task A_custom_team_role_keeps_its_vm_and_map_access(string[] before, string[] after)
    {
        await using var session = await fixture.BeginSessionAtMigrationAsync(BeforeVmAndMapPermissions);
        var roleId = await CreateTeamRole(session, "Red Cell", before);

        await session.MigrateAsync(cancellationToken: Ct);

        Assert.Equal(after, await TeamRoleGrants(session, roleId));
    }

    [Theory]
    [InlineData(new[] { "EditTeam" }, new[] { "ControlTeamVms" })]
    [InlineData(new[] { "EditView" }, new[] { "ControlViewVms" })]
    [InlineData(new[] { "ViewTeam", "ManageTeam" },
                new[] { "ManageTeam", "ManageTeamMaps", "ViewTeam", "ViewTeamMaps", "ViewTeamVms" })]
    [InlineData(new[] { "ViewView", "ManageView" },
                new[] { "ManageView", "ManageViewMaps", "ViewView", "ViewViewMaps", "ViewViewVms" })]
    public async Task A_team_keeps_the_vm_and_map_access_assigned_to_it_directly(string[] before, string[] after)
    {
        await using var session = await fixture.BeginSessionAtMigrationAsync(BeforeVmAndMapPermissions);
        var teamId = await CreateTeam(session, "Blue", before);

        await session.MigrateAsync(cancellationToken: Ct);

        Assert.Equal(after, await TeamAssignments(session, teamId));
    }

    [Fact]
    public async Task A_custom_system_role_keeps_its_system_wide_vm_and_map_access()
    {
        await using var session = await fixture.BeginSessionAtMigrationAsync(BeforeVmAndMapPermissions);
        var viewerId = await CreateSystemRole(session, "Range Viewer", "ViewViews");
        var operatorId = await CreateSystemRole(session, "Range Operator", "EditViews");
        var managerId = await CreateSystemRole(session, "Range Manager", "ManageViews");

        await session.MigrateAsync(cancellationToken: Ct);

        Assert.Equal(["ViewMaps", "ViewViews", "ViewVms"], await SystemRoleGrants(session, viewerId));
        Assert.Equal(["ControlVms", "EditViews"], await SystemRoleGrants(session, operatorId));
        Assert.Equal(["ManageMaps", "ManageViews", "ViewVms"], await SystemRoleGrants(session, managerId));
    }

    /// <summary>
    /// The migration once inserted the seeded grants of Observer and View Member unconditionally, so a
    /// deployment whose admin had deleted Observer failed on a foreign key and the API never started.
    /// </summary>
    [Fact]
    public async Task The_upgrade_succeeds_when_an_admin_deleted_Observer()
    {
        await using var session = await fixture.BeginSessionAtMigrationAsync(BeforeVmAndMapPermissions);
        await Execute(session, $"DELETE FROM team_role_permissions WHERE role_id = {ObserverRoleId}");
        await Execute(session, $"DELETE FROM team_roles WHERE id = {ObserverRoleId}");

        await session.MigrateAsync(cancellationToken: Ct);

        Assert.Empty(await TeamRoleGrants(session, ObserverRoleId));
        Assert.Contains("ControlTeamVms", await TeamRoleGrants(session, ViewMemberRoleId));
    }

    [Fact]
    public async Task The_upgrade_does_not_give_back_what_an_admin_took_from_View_Member()
    {
        await using var session = await fixture.BeginSessionAtMigrationAsync(BeforeVmAndMapPermissions);
        await Execute(session, $"""
            DELETE FROM team_role_permissions
            WHERE role_id = {ViewMemberRoleId}
              AND permission_id = (SELECT id FROM team_permissions WHERE name = 'EditTeam')
            """);

        await session.MigrateAsync(cancellationToken: Ct);

        Assert.DoesNotContain("ControlTeamVms", await TeamRoleGrants(session, ViewMemberRoleId));
    }

    [Fact]
    public async Task The_upgrade_preserves_additions_and_revocations_on_a_built_in_role()
    {
        await using var session = await fixture.BeginSessionAtMigrationAsync(BeforeVmAndMapPermissions);
        await Execute(session, $"""
            DELETE FROM team_role_permissions
            WHERE role_id = {ObserverRoleId}
              AND permission_id = (SELECT id FROM team_permissions WHERE name = 'ViewView')
            """);
        await Execute(session, $"""
            INSERT INTO team_role_permissions (role_id, permission_id)
            SELECT {ObserverRoleId}, id FROM team_permissions WHERE name IN ('EditView', 'ManageTeam')
            """);
        Assert.Equal(
            ["EditView", "ManageTeam", "ViewNetworks", "ViewTeam"],
            await TeamRoleGrants(session, ObserverRoleId));

        await session.MigrateAsync(cancellationToken: Ct);

        Assert.Equal(
            [
                "ControlViewVms", "ManageTeam", "ManageTeamMaps", "ViewNetworks", "ViewTeam",
                "ViewTeamMaps", "ViewTeamVms"
            ],
            await TeamRoleGrants(session, ObserverRoleId));
    }

    /// <summary>
    /// A built-in role holds every grant that the model seeds, with the seeded id, so EF's seed data
    /// keeps describing the database. A fresh install runs the same migrations from an empty database,
    /// so it ends up the same way. The migration maps each old grant on its own and does not remove a
    /// weaker grant that a stronger one covers, so the role also holds the extras listed here. They
    /// change no one's access.
    /// </summary>
    [Theory]
    [InlineData("c875dcce-2488-4e73-8585-8375b4730151", new[] { "ViewTeamMaps", "ViewTeamVms" })]
    [InlineData("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22", new[] { "ViewTeamVms" })]
    public async Task A_built_in_role_holds_every_seeded_grant_after_the_upgrade(string roleId, string[] extras)
    {
        var id = Guid.Parse(roleId);

        await using var session = await fixture.BeginSessionAtMigrationAsync(BeforeVmAndMapPermissions);
        await session.MigrateAsync(cancellationToken: Ct);

        await using var db = session.CreateContext();
        var permissionNames = await db.TeamPermissions.ToDictionaryAsync(x => x.Id, x => x.Name, Ct);
        var seeded = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(TeamRolePermissionEntity))!.GetSeedData()
            .Where(x => (Guid)x[nameof(TeamRolePermissionEntity.RoleId)] == id)
            .Select(x => $"{permissionNames[(Guid)x[nameof(TeamRolePermissionEntity.PermissionId)]]} {x[nameof(TeamRolePermissionEntity.Id)]}")
            .ToList();
        var migrated = await TeamRoleGrantsWithIds(session, id);

        Assert.Subset(migrated.ToHashSet(), seeded.ToHashSet());
        Assert.Equal(extras, migrated.Except(seeded).Select(x => x.Split(' ')[0]).Order(StringComparer.Ordinal));
    }

    // ---- Raw SQL helpers ------------------------------------------------------------------------

    private static async Task Execute(ITestDatabaseSession session, FormattableString sql)
    {
        await using var db = session.CreateContext();
        await db.Database.ExecuteSqlAsync(sql, Ct);
    }

    private static async Task<List<string>> Query(ITestDatabaseSession session, FormattableString sql)
    {
        await using var db = session.CreateContext();
        var rows = await db.Database.SqlQuery<string>(sql).ToListAsync(Ct);

        // Ordinal, so the order does not depend on the collation of the test container.
        return [.. rows.Order(StringComparer.Ordinal)];
    }

    private static async Task<Guid> CreateTeamRole(ITestDatabaseSession session, string name, string[] permissions)
    {
        var roleId = Guid.NewGuid();

        await Execute(session, $"""
            INSERT INTO team_roles (id, name, all_permissions, immutable) VALUES ({roleId}, {name}, false, false)
            """);
        await Execute(session, $"""
            INSERT INTO team_role_permissions (role_id, permission_id)
            SELECT {roleId}, id FROM team_permissions WHERE name = ANY({permissions})
            """);

        return roleId;
    }

    private static async Task<Guid> CreateTeam(ITestDatabaseSession session, string name, params string[] permissions)
    {
        var viewId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        await Execute(session, $"""
            INSERT INTO views (id, name, status, is_template, date_created)
            VALUES ({viewId}, 'Exercise', 0, false, now())
            """);
        await Execute(session, $"""
            INSERT INTO teams (id, name, view_id, role_id) VALUES ({teamId}, {name}, {viewId}, {ViewMemberRoleId})
            """);
        await Execute(session, $"""
            INSERT INTO team_permission_assignments (team_id, permission_id)
            SELECT {teamId}, id FROM team_permissions WHERE name = ANY({permissions})
            """);

        return teamId;
    }

    private static async Task<Guid> CreateSystemRole(ITestDatabaseSession session, string name, params string[] permissions)
    {
        var roleId = Guid.NewGuid();

        await Execute(session, $"""
            INSERT INTO roles (id, name, all_permissions, immutable) VALUES ({roleId}, {name}, false, false)
            """);
        await Execute(session, $"""
            INSERT INTO role_permissions (role_id, permission_id)
            SELECT {roleId}, id FROM permissions WHERE name = ANY({permissions})
            """);

        return roleId;
    }

    private static Task<List<string>> TeamRoleGrants(ITestDatabaseSession session, Guid roleId) =>
        Query(session, $"""
            SELECT p.name AS "Value"
            FROM team_role_permissions trp JOIN team_permissions p ON p.id = trp.permission_id
            WHERE trp.role_id = {roleId}
            """);

    private static Task<List<string>> TeamRoleGrantsWithIds(ITestDatabaseSession session, Guid roleId) =>
        Query(session, $"""
            SELECT p.name || ' ' || trp.id AS "Value"
            FROM team_role_permissions trp JOIN team_permissions p ON p.id = trp.permission_id
            WHERE trp.role_id = {roleId}
            """);

    private static Task<List<string>> TeamAssignments(ITestDatabaseSession session, Guid teamId) =>
        Query(session, $"""
            SELECT p.name AS "Value"
            FROM team_permission_assignments tpa JOIN team_permissions p ON p.id = tpa.permission_id
            WHERE tpa.team_id = {teamId}
            """);

    private static Task<List<string>> SystemRoleGrants(ITestDatabaseSession session, Guid roleId) =>
        Query(session, $"""
            SELECT p.name AS "Value"
            FROM role_permissions rp JOIN permissions p ON p.id = rp.permission_id
            WHERE rp.role_id = {roleId}
            """);
}
