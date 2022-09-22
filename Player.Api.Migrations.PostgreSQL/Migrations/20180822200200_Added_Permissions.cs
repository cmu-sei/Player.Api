// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class Added_Permissions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_users");

            migrationBuilder.DropTable(
                name: "team_users");

            migrationBuilder.DropColumn(
                name: "role",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role",
                table: "teams");

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "users",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "teams",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    key = table.Column<string>(nullable: true),
                    value = table.Column<string>(nullable: true),
                    description = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    team_id = table.Column<Guid>(nullable: false),
                    permission_id = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_team_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_permissions_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    user_id = table.Column<Guid>(nullable: false),
                    user_key = table.Column<int>(nullable: true),
                    permission_id = table.Column<Guid>(nullable: false)
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
                        name: "FK_user_permissions_users_user_key",
                        column: x => x.user_key,
                        principalTable: "users",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    role_id = table.Column<Guid>(nullable: false),
                    permission_id = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    team_id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: false),
                    exercise_membership_id = table.Column<Guid>(nullable: false),
                    role_id = table.Column<Guid>(nullable: true),
                    exercise_membership_entity_id = table.Column<Guid>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_team_memberships_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_team_memberships_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exercise_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    exercise_id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: false),
                    primary_team_membership_id = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_exercise_memberships_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exercise_memberships_team_memberships_primary_team_membersh~",
                        column: x => x.primary_team_membership_id,
                        principalTable: "team_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exercise_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_teams_role_id",
                table: "teams",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_memberships_primary_team_membership_id",
                table: "exercise_memberships",
                column: "primary_team_membership_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_memberships_user_id",
                table: "exercise_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_memberships_exercise_id_user_id",
                table: "exercise_memberships",
                columns: new[] { "exercise_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permissions_key_value",
                table: "permissions",
                columns: new[] { "key", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_role_id_permission_id",
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_exercise_membership_entity_id",
                table: "team_memberships",
                column: "exercise_membership_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_role_id",
                table: "team_memberships",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_user_id",
                table: "team_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_team_id_user_id",
                table: "team_memberships",
                columns: new[] { "team_id", "user_id" },
                unique: true);

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
                name: "IX_user_permissions_permission_id",
                table: "user_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_permissions_user_key",
                table: "user_permissions",
                column: "user_key");

            migrationBuilder.CreateIndex(
                name: "IX_user_permissions_user_id_permission_id",
                table: "user_permissions",
                columns: new[] { "user_id", "permission_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_teams_roles_role_id",
                table: "teams",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_users_roles_role_id",
                table: "users",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_team_memberships_exercise_memberships_exercise_membership_e~",
                table: "team_memberships",
                column: "exercise_membership_entity_id",
                principalTable: "exercise_memberships",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_teams_roles_role_id",
                table: "teams");

            migrationBuilder.DropForeignKey(
                name: "FK_users_roles_role_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "FK_exercise_memberships_team_memberships_primary_team_membersh~",
                table: "exercise_memberships");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "team_permissions");

            migrationBuilder.DropTable(
                name: "user_permissions");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "team_memberships");

            migrationBuilder.DropTable(
                name: "exercise_memberships");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropIndex(
                name: "IX_users_role_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_teams_role_id",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "teams");

            migrationBuilder.AddColumn<int>(
                name: "role",
                table: "users",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "role",
                table: "teams",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "exercise_users",
                columns: table => new
                {
                    exercise_id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: false),
                    primary_team_id = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_users", x => new { x.exercise_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_exercise_users_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exercise_users_teams_primary_team_id",
                        column: x => x.primary_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exercise_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_users",
                columns: table => new
                {
                    team_id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_users", x => new { x.team_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_team_users_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_users_primary_team_id",
                table: "exercise_users",
                column: "primary_team_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_users_user_id",
                table: "exercise_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_users_user_id",
                table: "team_users",
                column: "user_id");
        }
    }
}
