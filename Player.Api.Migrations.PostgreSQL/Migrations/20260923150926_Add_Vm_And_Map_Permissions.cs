// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Add_Vm_And_Map_Permissions : Migration
    {
        // vm.api used to read EditTeam and EditView as Vm control, and ViewTeam, ViewView, ManageTeam
        // and ManageView as Vm and Map access. It now reads only the permissions this migration adds, so
        // every existing grant of the old ones is copied to its new counterparts before EditTeam and
        // EditView are deleted. The copy runs over whatever roles and assignments the database holds,
        // not over the seeded rows, so custom roles keep their access and an admin's edits to the
        // built-in roles are respected.
        //
        // The SQL is kept here rather than shared, so this migration keeps doing what it did when it
        // shipped even if the permission model changes later.

        private const string TeamGrantMap = """
            grant_map(old_name, new_name) AS (VALUES
                ('EditTeam',   'ControlTeamVms'),
                ('EditView',   'ControlViewVms'),
                ('ViewTeam',   'ViewTeamVms'),
                ('ViewTeam',   'ViewTeamMaps'),
                ('ManageTeam', 'ViewTeamVms'),
                ('ManageTeam', 'ManageTeamMaps'),
                ('ViewView',   'ViewViewVms'),
                ('ViewView',   'ViewViewMaps'),
                ('ManageView', 'ViewViewVms'),
                ('ManageView', 'ManageViewMaps'))
            """;

        private const string TeamRoleGrants = $"""
            WITH {TeamGrantMap}
            INSERT INTO team_role_permissions (role_id, permission_id)
            SELECT DISTINCT trp.role_id, np.id
            FROM team_role_permissions trp
            JOIN team_permissions op ON op.id = trp.permission_id
            JOIN grant_map m ON m.old_name = op.name
            JOIN team_permissions np ON np.name = m.new_name
            ON CONFLICT (role_id, permission_id) DO NOTHING;
            """;

        private const string TeamAssignmentGrants = $"""
            WITH {TeamGrantMap}
            INSERT INTO team_permission_assignments (team_id, permission_id)
            SELECT DISTINCT tpa.team_id, np.id
            FROM team_permission_assignments tpa
            JOIN team_permissions op ON op.id = tpa.permission_id
            JOIN grant_map m ON m.old_name = op.name
            JOIN team_permissions np ON np.name = m.new_name
            ON CONFLICT (team_id, permission_id) DO NOTHING;
            """;

        // EditViews stays a system permission, but vm.api no longer reads it as Vm control.
        private const string SystemRoleGrants = """
            WITH grant_map(old_name, new_name) AS (VALUES
                ('ViewViews',   'ViewVms'),
                ('ViewViews',   'ViewMaps'),
                ('ManageViews', 'ViewVms'),
                ('ManageViews', 'ManageMaps'),
                ('EditViews',   'ControlVms'))
            INSERT INTO role_permissions (role_id, permission_id)
            SELECT DISTINCT rp.role_id, np.id
            FROM role_permissions rp
            JOIN permissions op ON op.id = rp.permission_id
            JOIN grant_map m ON m.old_name = op.name
            JOIN permissions np ON np.name = m.new_name
            ON CONFLICT (role_id, permission_id) DO NOTHING;
            """;

        // The copy above gave Observer and View Member their new grants with generated ids. These are
        // the ids the model seeds, so an upgraded database matches a fresh one row for row. A built-in
        // role that was deleted, or whose old grant an admin removed, has no row to update and gets
        // nothing.
        private const string RekeySeededGrants = """
            UPDATE team_role_permissions t SET id = s.id
            FROM (VALUES
                ('177b708b-ff6f-4381-8621-e6a163004ed9'::uuid, 'c875dcce-2488-4e73-8585-8375b4730151'::uuid, '2dabbfe3-957b-420e-9aa3-9e227494c836'::uuid),
                ('a2e44491-da6a-4042-bf9a-94b1dbf6a252'::uuid, 'c875dcce-2488-4e73-8585-8375b4730151'::uuid, '2baec4b7-5637-4e6c-8228-73e6fe38bbf5'::uuid),
                ('bb6ea1a2-b275-46cc-9043-34cd60d3daa6'::uuid, 'a721a3bf-0ae1-4cd3-9d6f-e56d07260f22'::uuid, 'dbc9b1c0-4964-439b-b7b4-3d6d0e4b9e91'::uuid),
                ('dced97e2-f3da-4d3f-ab1b-0f093ccf8f7f'::uuid, 'a721a3bf-0ae1-4cd3-9d6f-e56d07260f22'::uuid, '2a94e61f-5d26-4446-b47b-e4c5aeb854ba'::uuid)
            ) s(id, role_id, permission_id)
            WHERE t.role_id = s.role_id AND t.permission_id = s.permission_id;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("69556b84-4df2-46b6-9931-39e1e1cb0ccd"), "Allows viewing all Maps, but not creating, editing, or deleting them", false, "ViewMaps" },
                    { new Guid("71c8e1c1-d1e2-45b6-987f-823467b6fbb8"), "Allows viewing all Vm consoles, but not interacting with them", false, "ViewVms" },
                    { new Guid("92340801-3fbf-4f53-a075-02f576563b53"), "Allows interacting with and controlling all Vms", false, "ControlVms" },
                    { new Guid("f1403fb2-2925-40e4-aa94-5142bdf19ce8"), "Allows viewing, creating, editing, and deleting all Maps", false, "ManageMaps" }
                });

            migrationBuilder.InsertData(
                table: "team_permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("2a8d8276-c6e5-4f94-8e48-81e753700e89"), "Allows interacting with and controlling all of the Vms in the View", false, "ControlViewVms" },
                    { new Guid("2a94e61f-5d26-4446-b47b-e4c5aeb854ba"), "Allows viewing the Maps available to the Team, but not creating, editing, or deleting them", false, "ViewTeamMaps" },
                    { new Guid("2baec4b7-5637-4e6c-8228-73e6fe38bbf5"), "Allows viewing all of the Vm consoles in the View, but not interacting with them", false, "ViewViewVms" },
                    { new Guid("2dabbfe3-957b-420e-9aa3-9e227494c836"), "Allows viewing all of the Maps in the View, but not creating, editing, or deleting them", false, "ViewViewMaps" },
                    { new Guid("40deaedd-11dd-4b05-bcdd-045a249189b8"), "Allows viewing, creating, editing, and deleting all of the Maps available to the Team", false, "ManageTeamMaps" },
                    { new Guid("45a61f67-8f14-496b-b2cb-136fb7d81093"), "Allows viewing, creating, editing, and deleting all of the Maps in the View", false, "ManageViewMaps" },
                    { new Guid("dbc9b1c0-4964-439b-b7b4-3d6d0e4b9e91"), "Allows interacting with and controlling the Vms available to the Team", false, "ControlTeamVms" },
                    { new Guid("f75c335b-20f2-4eec-86b5-b21811c7b7db"), "Allows viewing the Vm consoles available to the Team, but not interacting with them", false, "ViewTeamVms" }
                });

            migrationBuilder.Sql(TeamRoleGrants);
            migrationBuilder.Sql(TeamAssignmentGrants);
            migrationBuilder.Sql(SystemRoleGrants);
            migrationBuilder.Sql(RekeySeededGrants);

            // Every grant of EditTeam and EditView now has a copy, so the cascade loses nothing.
            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("5ae96619-b40b-4fdb-bbef-ad476c21553d"));

            migrationBuilder.DeleteData(
                table: "team_role_permissions",
                keyColumn: "id",
                keyValue: new Guid("f83d8368-1839-44d4-ad8c-dfa7fae56565"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("69556b84-4df2-46b6-9931-39e1e1cb0ccd"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("71c8e1c1-d1e2-45b6-987f-823467b6fbb8"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("92340801-3fbf-4f53-a075-02f576563b53"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f1403fb2-2925-40e4-aa94-5142bdf19ce8"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("2a8d8276-c6e5-4f94-8e48-81e753700e89"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("40deaedd-11dd-4b05-bcdd-045a249189b8"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("45a61f67-8f14-496b-b2cb-136fb7d81093"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("f75c335b-20f2-4eec-86b5-b21811c7b7db"));

            migrationBuilder.DeleteData(
                table: "team_role_permissions",
                keyColumn: "id",
                keyValue: new Guid("177b708b-ff6f-4381-8621-e6a163004ed9"));

            migrationBuilder.DeleteData(
                table: "team_role_permissions",
                keyColumn: "id",
                keyValue: new Guid("a2e44491-da6a-4042-bf9a-94b1dbf6a252"));

            migrationBuilder.DeleteData(
                table: "team_role_permissions",
                keyColumn: "id",
                keyValue: new Guid("bb6ea1a2-b275-46cc-9043-34cd60d3daa6"));

            migrationBuilder.DeleteData(
                table: "team_role_permissions",
                keyColumn: "id",
                keyValue: new Guid("dced97e2-f3da-4d3f-ab1b-0f093ccf8f7f"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("2a94e61f-5d26-4446-b47b-e4c5aeb854ba"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("2baec4b7-5637-4e6c-8228-73e6fe38bbf5"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("2dabbfe3-957b-420e-9aa3-9e227494c836"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("dbc9b1c0-4964-439b-b7b4-3d6d0e4b9e91"));

            migrationBuilder.InsertData(
                table: "team_permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("5ae96619-b40b-4fdb-bbef-ad476c21553d"), "Allows editing all basic resources in the View, including making changes within Virtual Machines, if applicable", true, "EditView" },
                    { new Guid("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"), "Allows editing basic Team resources, including making changes within Virtual Machines, if applicable", true, "EditTeam" }
                });

            migrationBuilder.InsertData(
                table: "team_role_permissions",
                columns: new[] { "id", "permission_id", "role_id" },
                values: new object[] { new Guid("f83d8368-1839-44d4-ad8c-dfa7fae56565"), new Guid("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"), new Guid("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22") });
        }
    }
}
