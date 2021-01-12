// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class Add_ExerciseUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_exercise_users_primary_team_id",
                table: "exercise_users",
                column: "primary_team_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_users_user_id",
                table: "exercise_users",
                column: "user_id");

            migrationBuilder.Sql(
                @"INSERT INTO exercise_users
                (
	                exercise_id,
	                user_id,
	                primary_team_id
                )
                SELECT teams.exercise_id, team_users.user_id, team_users.team_id
                FROM public.team_users
                INNER JOIN teams on team_users.team_id = teams.id"
                );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_users");
        }
    }
}
