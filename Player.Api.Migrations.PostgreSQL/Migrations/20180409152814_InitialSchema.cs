// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;
using System.Collections.Generic;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class InitialSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", "'uuid-ossp', '', ''");

            migrationBuilder.CreateTable(
                name: "application_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    embeddable = table.Column<bool>(nullable: false),
                    icon = table.Column<string>(nullable: true),
                    name = table.Column<string>(nullable: true),
                    url = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exercises",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    description = table.Column<string>(nullable: true),
                    name = table.Column<string>(nullable: true),
                    status = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercises", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    key = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    id = table.Column<Guid>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    role = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.key);
                    table.UniqueConstraint("AK_users_id", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    application_template_id = table.Column<Guid>(nullable: true),
                    embeddable = table.Column<bool>(nullable: true),
                    exercise_id = table.Column<Guid>(nullable: false),
                    icon = table.Column<string>(nullable: true),
                    name = table.Column<string>(nullable: true),
                    url = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.id);
                    table.ForeignKey(
                        name: "FK_applications_application_templates_application_template_id",
                        column: x => x.application_template_id,
                        principalTable: "application_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_applications_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    exercise_id = table.Column<Guid>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    role = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.id);
                    table.ForeignKey(
                        name: "FK_teams_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    application_id = table.Column<Guid>(nullable: false),
                    display_order = table.Column<float>(nullable: false),
                    team_id = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_instances", x => x.id);
                    table.ForeignKey(
                        name: "FK_application_instances_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_application_instances_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
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
                name: "IX_application_instances_application_id",
                table: "application_instances",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_instances_team_id",
                table: "application_instances",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_application_template_id",
                table: "applications",
                column: "application_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_exercise_id",
                table: "applications",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_users_user_id",
                table: "team_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_teams_exercise_id",
                table: "teams",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_id",
                table: "users",
                column: "id",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_instances");

            migrationBuilder.DropTable(
                name: "team_users");

            migrationBuilder.DropTable(
                name: "applications");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "application_templates");

            migrationBuilder.DropTable(
                name: "exercises");
        }
    }
}
