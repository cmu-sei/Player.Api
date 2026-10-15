// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Player.Api.Migrations.PostgreSQL.Migrations;
using Player.Api.Tests.Support;

namespace Player.Api.Tests.Migrations;

/// <summary>
/// No migration takes a permission away from a team, a team role or a system role by accident. Each
/// test stops at one migration, gives the database the custom roles and teams a deployment might hold,
/// applies every later migration, and checks that each of them still holds every permission it held.
/// </summary>
/// <remarks>
/// <para>
/// A permission that a migration deletes or renames must be listed in <see cref="Retired"/> with the
/// permissions that replace it. Its holders must then hold those instead. This is what makes the test
/// generic: a later migration needs no test of its own, only an entry here when it retires a
/// permission on purpose.
/// </para>
/// <para>
/// The seed reads the permission tables rather than naming permissions, so it gives the database a
/// holder for every permission that exists at the migration it stops at, including ones added after
/// this test was written.
/// </para>
/// </remarks>
public class GrantPreservationTests(DatabaseFixture fixture)
{
    /// <summary>
    /// The first migration a test stops at. Every migration after it is checked against custom data.
    /// </summary>
    private const string Baseline = "20260922155600_Add_User_Identity_Attributes";

    /// <summary>
    /// Permissions that a migration deleted or renamed on purpose, and what each holder gets instead.
    /// An empty list means holders deliberately lose it. Chains resolve, so an entry can name a
    /// permission that a later entry retires in turn.
    /// </summary>
    private static readonly Dictionary<string, string[]> Retired = new()
    {
        // Add_Vm_And_Map_Permissions
        ["EditTeam"] = ["ControlTeamVms"],
        ["EditView"] = ["ControlViewVms"],
    };

    /// <summary>The kinds of holder a grant can have, as <see cref="GrantsQuery"/> names them.</summary>
    private const string TeamRole = "team role";
    private const string SystemRole = "system role";
    private const string TeamAssignment = "team assignment";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every migration from <see cref="Baseline"/> on, except the latest, which has nothing after it, once
    /// for each kind of holder.
    /// </summary>
    public static TheoryData<string, string> StartingMigrationsAndHolders()
    {
        var ids = typeof(Add_Vm_And_Map_Permissions).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(id => id is not null)
            .Order(StringComparer.Ordinal)
            .ToList();
        var starts = ids.Where(id => string.CompareOrdinal(id, Baseline) >= 0).SkipLast(1);

        return [.. starts.SelectMany(id => new[] { TeamRole, SystemRole, TeamAssignment }.Select(holder => (id, holder)))];
    }

    [Theory]
    [MemberData(nameof(StartingMigrationsAndHolders))]
    public async Task Later_migrations_keep_every_grant(string startingMigration, string holder)
    {
        await using var session = await fixture.BeginSessionAtMigrationAsync(startingMigration);
        await Execute(session, SeedCustomRolesAndTeams);
        var before = await Grants(session, holder);

        await session.MigrateAsync(cancellationToken: Ct);

        var after = await Grants(session, holder);
        var permissions = (await Query(session, PermissionNames)).ToHashSet();

        // A seed that silently inserts nothing would make every migration pass.
        Assert.NotEmpty(before);
        Assert.Empty(Unreplaced(before, permissions));
        Assert.Empty(Lost(before, after, permissions));
    }

    /// <summary>
    /// The grants whose permission no longer exists and is not in <see cref="Retired"/>. If a migration
    /// retired one on purpose, add it to <see cref="Retired"/> with what replaces it.
    /// </summary>
    private static List<Grant> Unreplaced(List<Grant> before, HashSet<string> permissions) =>
        [.. before.Where(grant => Resolve(grant.Permission, permissions) is null)];

    /// <summary>
    /// The grants a holder should hold after the migrations and does not: the permission it held, or,
    /// for a retired one, each permission that replaces it (<see cref="Grant.InPlaceOf"/> names the old one).
    /// </summary>
    private static List<Grant> Lost(List<Grant> before, List<Grant> after, HashSet<string> permissions) =>
        [.. before
            .SelectMany(grant => (Resolve(grant.Permission, permissions) ?? [])
                .Select(permission => grant with
                {
                    Permission = permission,
                    InPlaceOf = permission == grant.Permission ? null : grant.Permission
                }))
            .Where(required => !after.Contains(required))];

    /// <summary>
    /// The permissions a holder of <paramref name="permission"/> must hold after the migrations, or
    /// <c>null</c> when it is gone and <see cref="Retired"/> does not say what replaces it.
    /// </summary>
    private static HashSet<string> Resolve(string permission, HashSet<string> existing)
    {
        if (Retired.TryGetValue(permission, out var replacements))
        {
            var resolved = new HashSet<string>();

            foreach (var replacement in replacements)
            {
                var next = Resolve(replacement, existing);

                if (next is null)
                    return null;

                resolved.UnionWith(next);
            }

            return resolved;
        }

        return existing.Contains(permission) ? [permission] : null;
    }

    /// <summary>
    /// For each kind of holder: one holder per permission, so a lost grant cannot hide behind another
    /// grant of the same holder, and one holder with every permission. The built-in roles keep their
    /// seeded grants and are checked as they are.
    /// </summary>
    private const string SeedCustomRolesAndTeams = """
        INSERT INTO team_roles (id, name, all_permissions, immutable)
        SELECT gen_random_uuid(), 'Custom team role: ' || name, false, false FROM team_permissions
        UNION ALL SELECT gen_random_uuid(), 'Custom team role: all', false, false;

        INSERT INTO team_role_permissions (role_id, permission_id)
        SELECT r.id, p.id FROM team_roles r JOIN team_permissions p
            ON r.name IN ('Custom team role: ' || p.name, 'Custom team role: all');

        INSERT INTO roles (id, name, all_permissions, immutable)
        SELECT gen_random_uuid(), 'Custom system role: ' || name, false, false FROM permissions
        UNION ALL SELECT gen_random_uuid(), 'Custom system role: all', false, false;

        INSERT INTO role_permissions (role_id, permission_id)
        SELECT r.id, p.id FROM roles r JOIN permissions p
            ON r.name IN ('Custom system role: ' || p.name, 'Custom system role: all');

        INSERT INTO views (id, name, status, is_template, date_created)
        VALUES ('9a3c4f8e-5b1d-4c2a-8e7f-1d2c3b4a5f60', 'Grant preservation', 0, false, now());

        INSERT INTO teams (id, name, view_id, role_id)
        SELECT gen_random_uuid(), 'Team with ' || name, '9a3c4f8e-5b1d-4c2a-8e7f-1d2c3b4a5f60'::uuid,
            (SELECT id FROM team_roles WHERE name = 'Custom team role: all')
        FROM team_permissions
        UNION ALL SELECT gen_random_uuid(), 'Team with all', '9a3c4f8e-5b1d-4c2a-8e7f-1d2c3b4a5f60'::uuid,
            (SELECT id FROM team_roles WHERE name = 'Custom team role: all');

        INSERT INTO team_permission_assignments (team_id, permission_id)
        SELECT t.id, p.id FROM teams t JOIN team_permissions p
            ON t.name IN ('Team with ' || p.name, 'Team with all');
        """;

    private const string GrantsQuery = $"""
        SELECT '{TeamRole} ' || r.id || chr(9) || r.name || chr(9) || p.name AS "Value"
        FROM team_role_permissions x
        JOIN team_roles r ON r.id = x.role_id JOIN team_permissions p ON p.id = x.permission_id
        UNION ALL
        SELECT '{SystemRole} ' || r.id || chr(9) || r.name || chr(9) || p.name
        FROM role_permissions x
        JOIN roles r ON r.id = x.role_id JOIN permissions p ON p.id = x.permission_id
        UNION ALL
        SELECT '{TeamAssignment} ' || t.id || chr(9) || t.name || chr(9) || p.name
        FROM team_permission_assignments x
        JOIN teams t ON t.id = x.team_id JOIN team_permissions p ON p.id = x.permission_id
        """;

    private const string PermissionNames = """
        SELECT name AS "Value" FROM team_permissions UNION SELECT name FROM permissions
        """;

    /// <summary>
    /// A grant, keyed by the kind and id of its holder. <see cref="Name"/> and <see cref="InPlaceOf"/> are
    /// for the failure message only and take no part in equality, so a migration that renames a role does
    /// not count as a loss.
    /// </summary>
    private sealed record Grant(string Holder, string Permission)
    {
        public string Name { get; init; }

        /// <summary>The retired permission the holder held, where <see cref="Permission"/> replaces it.</summary>
        public string InPlaceOf { get; init; }

        public bool Equals(Grant other) => other is not null && Holder == other.Holder && Permission == other.Permission;

        public override int GetHashCode() => HashCode.Combine(Holder, Permission);

        public override string ToString() =>
            InPlaceOf is null ? $"{Holder} ({Name}): {Permission}" : $"{Holder} ({Name}): {Permission} in place of {InPlaceOf}";
    }

    /// <summary>Every grant held by a holder of the given kind.</summary>
    private static async Task<List<Grant>> Grants(UpgradeTestSession session, string holder) =>
        [.. (await Query(session, GrantsQuery))
            .Select(row => row.Split('\t'))
            .Select(parts => new Grant(parts[0], parts[2]) { Name = parts[1] })
            .Where(grant => grant.Holder.StartsWith(holder + " ", StringComparison.Ordinal))];

    private static async Task Execute(UpgradeTestSession session, string sql)
    {
        await using var db = session.CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql, Ct);
    }

    private static async Task<List<string>> Query(UpgradeTestSession session, string sql)
    {
        await using var db = session.CreateContext();
        return await db.Database.SqlQueryRaw<string>(sql).ToListAsync(Ct);
    }
}
