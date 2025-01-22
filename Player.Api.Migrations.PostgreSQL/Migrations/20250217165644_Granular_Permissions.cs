using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Granular_Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Begin manual data saving
            migrationBuilder.CreateTable(
                name: "migration_user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_name = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_user_roles", x => x.user_id);
                    table.UniqueConstraint("UX_user_id", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "migration_team_roles",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_name = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_team_roles", x => x.team_id);
                    table.UniqueConstraint("UX_team_id", x => x.team_id);
                });

            migrationBuilder.CreateTable(
                name: "migration_team_membership_roles",
                columns: table => new
                {
                    team_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_name = table.Column<string>(type: "text", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_team_membership_roles", x => x.team_membership_id);
                    table.UniqueConstraint("UX_team_membership_id", x => x.team_membership_id);
                });

            migrationBuilder.Sql(@"
                INSERT INTO migration_user_roles (user_id, role_name)
                SELECT DISTINCT
                    u.id AS user_id,
                    CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM user_permissions up2
                            JOIN permissions p2 ON up2.permission_id = p2.id
                            WHERE up2.user_id = u.id AND p2.key = 'ReadOnly'
                        )
                        OR EXISTS (
                            SELECT 1
                            FROM role_permissions rp2
                            JOIN permissions p3 ON rp2.permission_id = p3.id
                            WHERE rp2.role_id = u.role_id AND p3.key = 'ReadOnly'
                        ) THEN 'Observer'
                        ELSE 'Administrator'
                    END AS role_name
                FROM users u
                LEFT JOIN user_permissions up ON u.id = up.user_id
                LEFT JOIN permissions p ON up.permission_id = p.id
                LEFT JOIN role_permissions rp ON u.role_id = rp.role_id
                LEFT JOIN permissions p_role ON rp.permission_id = p_role.id
                WHERE p.key = 'SystemAdmin'
                    OR p_role.key = 'SystemAdmin'
                GROUP BY u.id, u.role_id
                ON CONFLICT (user_id) DO NOTHING;");

            migrationBuilder.Sql(@"
                INSERT INTO migration_team_roles (team_id, role_name)
                SELECT DISTINCT
                    t.id AS team_id,
                    CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM team_permissions tp2
                            JOIN permissions p2 ON tp2.permission_id = p2.id
                            WHERE tp2.team_id = t.id AND p2.key = 'ReadOnly'
                        )
                        OR EXISTS (
                            SELECT 1
                            FROM role_permissions rp2
                            JOIN permissions p3 ON rp2.permission_id = p3.id
                            WHERE rp2.role_id = t.role_id AND p3.key = 'ReadOnly'
                        ) THEN 'Observer'
                        ELSE 'View Admin'
                    END AS role_name
                FROM teams t
                LEFT JOIN role_permissions rp ON t.role_id = rp.role_id
                LEFT JOIN permissions p_role ON rp.permission_id = p_role.id
                LEFT JOIN team_permissions tp ON t.id = tp.team_id
                LEFT JOIN permissions p_team ON tp.permission_id = p_team.id
                WHERE p_role.key = 'ViewAdmin'
                OR p_team.key = 'ViewAdmin'
                ON CONFLICT (team_id) DO NOTHING;");

            migrationBuilder.Sql(@"
                INSERT INTO migration_team_membership_roles (team_membership_id, role_name)
                SELECT DISTINCT
                    tm.id AS team_membership_id,
                    CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM role_permissions rp2
                            JOIN permissions p2 ON rp2.permission_id = p2.id
                            WHERE rp2.role_id = tm.role_id AND p2.key = 'ReadOnly'
                        )
                        AND EXISTS (
                            SELECT 1
                            FROM role_permissions rp3
                            JOIN permissions p3 ON rp3.permission_id = p3.id
                            WHERE rp3.role_id = tm.role_id AND p3.key = 'ViewAdmin'
                        ) THEN 'Observer'
                        WHEN EXISTS (
                            SELECT 1
                            FROM role_permissions rp2
                            JOIN permissions p2 ON rp2.permission_id = p2.id
                            WHERE rp2.role_id = tm.role_id AND p2.key = 'ViewAdmin'
                        ) THEN 'View Admin'
                        ELSE NULL
                    END AS role_name
                FROM team_memberships tm
                JOIN roles r ON tm.role_id = r.id
                LEFT JOIN role_permissions rp ON r.id = rp.role_id
                LEFT JOIN permissions p ON rp.permission_id = p.id
                WHERE p.key IN ('ReadOnly', 'ViewAdmin')
                ON CONFLICT (team_membership_id) DO NOTHING;");

            migrationBuilder.Sql(@"DELETE FROM team_permissions;");
            migrationBuilder.Sql(@"DELETE FROM permissions;");
            migrationBuilder.Sql(@"UPDATE team_memberships SET role_id = null");
            migrationBuilder.Sql(@"UPDATE teams SET role_id = null");
            migrationBuilder.Sql(@"UPDATE users SET role_id = null");
            // End manual data saving

            migrationBuilder.DropForeignKey(
                name: "FK_team_memberships_roles_role_id",
                table: "team_memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_team_permissions_permissions_permission_id",
                table: "team_permissions");

            migrationBuilder.DropForeignKey(
                name: "FK_team_permissions_teams_team_id",
                table: "team_permissions");

            migrationBuilder.DropForeignKey(
                name: "FK_teams_roles_role_id",
                table: "teams");

            migrationBuilder.DropTable(
                name: "user_permissions");

            migrationBuilder.DropIndex(
                name: "IX_team_permissions_permission_id",
                table: "team_permissions");

            migrationBuilder.DropIndex(
                name: "IX_team_permissions_team_id_permission_id",
                table: "team_permissions");

            migrationBuilder.DropIndex(
                name: "IX_permissions_key_value",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "permission_id",
                table: "team_permissions");

            migrationBuilder.DropColumn(
                name: "team_id",
                table: "team_permissions");

            migrationBuilder.DropColumn(
                name: "key",
                table: "permissions");

            migrationBuilder.RenameColumn(
                name: "value",
                table: "permissions",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "read_only",
                table: "permissions",
                newName: "immutable");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "team_permissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "immutable",
                table: "team_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "team_permissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "all_permissions",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "immutable",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "team_permission_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_permission_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_team_permission_assignments_team_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "team_permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_permission_assignments_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    all_permissions = table.Column<bool>(type: "boolean", nullable: false),
                    immutable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_role_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_role_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_team_role_permissions_team_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "team_permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_role_permissions_team_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "team_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("06da87d8-7636-4a50-929a-bbff2fbad548"), "", true, "ViewViews" },
                    { new Guid("06e2699d-21a9-4053-922a-411499b3e923"), "", true, "CreateViews" },
                    { new Guid("0ef125ff-c493-476d-a041-0b6af54f4d36"), "", true, "ViewApplications" },
                    { new Guid("5597959a-ff30-4b73-9122-f21d17c19382"), "", true, "ManageViews" },
                    { new Guid("5cc1e12e-7440-4bd8-9a54-ccb5bb0f3f1e"), "", true, "ManageUsers" },
                    { new Guid("6c407f81-ab2e-4b24-911b-78b7f424b965"), "", true, "EditViews" },
                    { new Guid("70f3e368-7e7a-4166-9698-5c96dbb19ceb"), "", true, "ViewUsers" },
                    { new Guid("8dc72622-565d-4b86-b6d7-1692dc803815"), "", true, "ManageApplications" },
                    { new Guid("cfcc8ac3-6591-41b8-abe1-0456616b3798"), "", true, "ViewRoles" },
                    { new Guid("e15b0177-5250-4886-b062-4029a9371a99"), "", true, "ViewWebhookSubscriptions" },
                    { new Guid("e1772ce2-eacb-478f-bac8-2e77d49c608a"), "", true, "ManageWebhookSubscriptions" },
                    { new Guid("f1416f76-aa64-4edc-bfa8-6f234da85060"), "", true, "ManageRoles" }
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "all_permissions", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("7fd6aa3e-a765-47b8-a77e-f58eae53a82f"), false, false, "Content Developer" },
                    { new Guid("f6c07d62-4f2c-4bd5-82af-bf32c0daccc7"), true, true, "Administrator" }
                });

            migrationBuilder.InsertData(
                table: "team_permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("3b135496-c7d9-4bef-b60c-fbcfa1af9c1b"), "Allows downloading files directly from Vms", false, "DownloadVmFiles" },
                    { new Guid("5ae96619-b40b-4fdb-bbef-ad476c21553d"), "Allows editing all basic resources in the View, including making changes within Virtual Machines, if applicable.", true, "EditView" },
                    { new Guid("5da3014c-a6a5-4c3c-a658-e86672801313"), "Allows uploading ISOs that can be used by any Teams in the View", false, "UploadViewIsos" },
                    { new Guid("6e41449b-a5da-4ac0-9adb-432210a5541c"), "Allows uploading files directly to Vms", false, "UploadVmFiles" },
                    { new Guid("7be07cd5-104e-4770-800b-80ac26cda6d5"), "Allows viewing all resources in the View", true, "ViewView" },
                    { new Guid("83e41563-8b7f-4f43-b9d0-2d8dc12fc0bf"), "Allows managing all Team resources, including adding and removing Users.", true, "ManageTeam" },
                    { new Guid("d7271fd0-e47f-4630-a5ef-744acc4dc004"), "Allows uploading ISOs that can be used by members of the Team", false, "UploadTeamIsos" },
                    { new Guid("dedb382b-9d9f-43dc-b128-bb5f1ad94a15"), "Allows managing all resources for all Teams in the View", true, "ManageView" },
                    { new Guid("f3ef9465-7f7c-43ef-9855-83798ce5bcd5"), "Allows viewing Team resources", true, "ViewTeam" },
                    { new Guid("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"), "Allows editing basic Team resources, including making changes within Virtual Machines, if applicable.", true, "EditTeam" }
                });

            migrationBuilder.InsertData(
                table: "team_roles",
                columns: new[] { "id", "all_permissions", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22"), false, false, "View Member" },
                    { new Guid("b65ce1b0-f995-45e1-93fc-47a09542cee5"), true, true, "View Admin" },
                    { new Guid("c875dcce-2488-4e73-8585-8375b4730151"), false, false, "Observer" }
                });

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "teams",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "id", "permission_id", "role_id" },
                values: new object[] { new Guid("b5ef76d0-6257-4657-a51a-79d1e3850720"), new Guid("06e2699d-21a9-4053-922a-411499b3e923"), new Guid("7fd6aa3e-a765-47b8-a77e-f58eae53a82f") });

            migrationBuilder.InsertData(
                table: "team_role_permissions",
                columns: new[] { "id", "permission_id", "role_id" },
                values: new object[,]
                {
                    { new Guid("5939d9cf-6f4b-4136-907c-0e878da2241b"), new Guid("7be07cd5-104e-4770-800b-80ac26cda6d5"), new Guid("c875dcce-2488-4e73-8585-8375b4730151") },
                    { new Guid("8a2d8db9-cb8f-4952-9f3b-1377140f0c11"), new Guid("d7271fd0-e47f-4630-a5ef-744acc4dc004"), new Guid("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22") },
                    { new Guid("aba4f8e0-298e-4e10-b0d4-ec6447baad6b"), new Guid("6e41449b-a5da-4ac0-9adb-432210a5541c"), new Guid("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22") },
                    { new Guid("d0715a73-16c3-4e44-be57-6bbf2e25e7b3"), new Guid("f3ef9465-7f7c-43ef-9855-83798ce5bcd5"), new Guid("c875dcce-2488-4e73-8585-8375b4730151") },
                    { new Guid("d1252d24-c25d-4a80-a91c-4ad23efa9f89"), new Guid("f3ef9465-7f7c-43ef-9855-83798ce5bcd5"), new Guid("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22") },
                    { new Guid("f83d8368-1839-44d4-ad8c-dfa7fae56565"), new Guid("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"), new Guid("a721a3bf-0ae1-4cd3-9d6f-e56d07260f22") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_team_permissions_name",
                table: "team_permissions",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permissions_name",
                table: "permissions",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_permission_assignments_permission_id",
                table: "team_permission_assignments",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_permission_assignments_team_id_permission_id",
                table: "team_permission_assignments",
                columns: new[] { "team_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_role_permissions_permission_id",
                table: "team_role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_role_permissions_role_id_permission_id",
                table: "team_role_permissions",
                columns: new[] { "role_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_roles_name",
                table: "team_roles",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_team_memberships_team_roles_role_id",
                table: "team_memberships",
                column: "role_id",
                principalTable: "team_roles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_teams_team_roles_role_id",
                table: "teams",
                column: "role_id",
                principalTable: "team_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Begin manual data migration
            migrationBuilder.Sql(@"
                UPDATE users u
                SET role_id = r.id
                FROM migration_user_roles mur
                JOIN roles r ON mur.role_name = r.name
                WHERE u.id = mur.user_id;");

            migrationBuilder.Sql(@"
                UPDATE teams t
                SET role_id = r.id
                FROM migration_team_roles mtr
                JOIN team_roles r ON mtr.role_name = r.name
                WHERE t.id = mtr.team_id;");

            migrationBuilder.Sql(@"
                UPDATE teams t
                SET role_id = r.id
                FROM roles r
                WHERE t.role_id IS NULL
                    AND r.name = 'View Member';");

            migrationBuilder.Sql(@"
                UPDATE team_memberships t
                SET role_id = r.id
                FROM migration_team_membership_roles mtmr
                JOIN team_roles r ON mtmr.role_name = r.name
                WHERE t.id = mtmr.team_membership_id;");

            migrationBuilder.DropTable(
                name: "migration_user_roles"
            );

            migrationBuilder.DropTable(
                name: "migration_team_roles"
            );

            migrationBuilder.DropTable(
                name: "migration_team_membership_roles"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_team_memberships_team_roles_role_id",
                table: "team_memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_teams_team_roles_role_id",
                table: "teams");

            migrationBuilder.DropTable(
                name: "team_permission_assignments");

            migrationBuilder.DropTable(
                name: "team_role_permissions");

            migrationBuilder.DropTable(
                name: "team_roles");

            migrationBuilder.DropIndex(
                name: "IX_team_permissions_name",
                table: "team_permissions");

            migrationBuilder.DropIndex(
                name: "IX_permissions_name",
                table: "permissions");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("06da87d8-7636-4a50-929a-bbff2fbad548"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0ef125ff-c493-476d-a041-0b6af54f4d36"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5597959a-ff30-4b73-9122-f21d17c19382"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5cc1e12e-7440-4bd8-9a54-ccb5bb0f3f1e"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("6c407f81-ab2e-4b24-911b-78b7f424b965"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("70f3e368-7e7a-4166-9698-5c96dbb19ceb"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("8dc72622-565d-4b86-b6d7-1692dc803815"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cfcc8ac3-6591-41b8-abe1-0456616b3798"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("e15b0177-5250-4886-b062-4029a9371a99"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("e1772ce2-eacb-478f-bac8-2e77d49c608a"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f1416f76-aa64-4edc-bfa8-6f234da85060"));

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumn: "id",
                keyValue: new Guid("b5ef76d0-6257-4657-a51a-79d1e3850720"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("f6c07d62-4f2c-4bd5-82af-bf32c0daccc7"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("3b135496-c7d9-4bef-b60c-fbcfa1af9c1b"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("5ae96619-b40b-4fdb-bbef-ad476c21553d"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("5da3014c-a6a5-4c3c-a658-e86672801313"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("6e41449b-a5da-4ac0-9adb-432210a5541c"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("7be07cd5-104e-4770-800b-80ac26cda6d5"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("83e41563-8b7f-4f43-b9d0-2d8dc12fc0bf"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("d7271fd0-e47f-4630-a5ef-744acc4dc004"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("dedb382b-9d9f-43dc-b128-bb5f1ad94a15"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("f3ef9465-7f7c-43ef-9855-83798ce5bcd5"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("06e2699d-21a9-4053-922a-411499b3e923"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("7fd6aa3e-a765-47b8-a77e-f58eae53a82f"));

            migrationBuilder.DropColumn(
                name: "description",
                table: "team_permissions");

            migrationBuilder.DropColumn(
                name: "immutable",
                table: "team_permissions");

            migrationBuilder.DropColumn(
                name: "name",
                table: "team_permissions");

            migrationBuilder.DropColumn(
                name: "all_permissions",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "immutable",
                table: "roles");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "permissions",
                newName: "value");

            migrationBuilder.RenameColumn(
                name: "immutable",
                table: "permissions",
                newName: "read_only");

            migrationBuilder.AlterColumn<Guid>(
                name: "role_id",
                table: "teams",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "permission_id",
                table: "team_permissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "team_id",
                table: "team_permissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "key",
                table: "permissions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_permissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_team_permissions_permission_id",
                table: "team_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_permissions_team_id_permission_id",
                table: "team_permissions",
                columns: new[] { "team_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permissions_key_value",
                table: "permissions",
                columns: new[] { "key", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_permissions_permission_id",
                table: "user_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_permissions_user_id_permission_id",
                table: "user_permissions",
                columns: new[] { "user_id", "permission_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_team_memberships_roles_role_id",
                table: "team_memberships",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_team_permissions_permissions_permission_id",
                table: "team_permissions",
                column: "permission_id",
                principalTable: "permissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_team_permissions_teams_team_id",
                table: "team_permissions",
                column: "team_id",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_teams_roles_role_id",
                table: "teams",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id");
        }
    }
}
