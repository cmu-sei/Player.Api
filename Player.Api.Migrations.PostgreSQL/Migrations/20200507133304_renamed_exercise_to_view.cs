// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class renamed_exercise_to_view : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_applications_exercises_exercise_id",
                table: "applications");

            migrationBuilder.DropForeignKey(
                name: "FK_team_memberships_exercise_memberships_exercise_membership_id",
                table: "team_memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_teams_exercises_exercise_id",
                table: "teams");

            migrationBuilder.RenameColumn(
                name: "exercise_id",
                table: "teams",
                newName: "view_id");

            migrationBuilder.RenameIndex(
                name: "IX_teams_exercise_id",
                table: "teams",
                newName: "IX_teams_view_id");

            migrationBuilder.RenameColumn(
                name: "exercise_membership_id",
                table: "team_memberships",
                newName: "view_membership_id");

            migrationBuilder.RenameIndex(
                name: "IX_team_memberships_exercise_membership_id",
                table: "team_memberships",
                newName: "IX_team_memberships_view_membership_id");

            migrationBuilder.RenameColumn(
                name: "exercise_id",
                table: "notifications",
                newName: "view_id");

            migrationBuilder.RenameColumn(
                name: "exercise_id",
                table: "applications",
                newName: "view_id");

            migrationBuilder.RenameIndex(
                name: "IX_applications_exercise_id",
                table: "applications",
                newName: "IX_applications_view_id");

            migrationBuilder.RenameTable(
                name: "exercises",
                newName: "views");

            migrationBuilder.RenameTable(
                name: "exercise_memberships",
                newName: "view_memberships");

            migrationBuilder.RenameColumn(
                name: "exercise_id",
                table: "view_memberships",
                newName: "view_id");

            migrationBuilder.RenameIndex(
                name: "IX_exercise_memberships_primary_team_membership_id",
                table: "view_memberships",
                newName: "IX_view_memberships_primary_team_membership_id");

            migrationBuilder.RenameIndex(
                name: "IX_exercise_memberships_user_id",
                table: "view_memberships",
                newName: "IX_view_memberships_user_id");

            migrationBuilder.DropIndex(
                name: "IX_exercise_memberships_exercise_id_user_id",
                table: "view_memberships"
            );

            migrationBuilder.CreateIndex(
                name: "IX_view_memberships_view_id_user_id",
                table: "view_memberships",
                columns: new[] { "view_id", "user_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_applications_views_view_id",
                table: "applications",
                column: "view_id",
                principalTable: "views",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_team_memberships_view_memberships_view_membership_id",
                table: "team_memberships",
                column: "view_membership_id",
                principalTable: "view_memberships",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_teams_views_view_id",
                table: "teams",
                column: "view_id",
                principalTable: "views",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(
                @"UPDATE permissions
                SET key='ViewAdmin'
                WHERE key='ExerciseAdmin'"
            );

            migrationBuilder.Sql(
                @"UPDATE application_templates
                    SET url = REPLACE(REPLACE(url, '{exerciseId}','{viewId}'), '{exerciseName}', '{viewName}')"
            );

            migrationBuilder.Sql(
                @"UPDATE applications
                    SET url = REPLACE(REPLACE(url, '{exerciseId}','{viewId}'), '{exerciseName}', '{viewName}')"
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"UPDATE permissions
                SET key='ExerciseAdmin'
                WHERE key='ViewAdmin'"
            );

            migrationBuilder.Sql(
                @"UPDATE application_templates
                    SET url = REPLACE(REPLACE(url, '{viewId}','{exerciseId}'), '{viewName}', '{exerciseName}')"
            );

            migrationBuilder.Sql(
                @"UPDATE applications
                    SET url = REPLACE(REPLACE(url, '{viewId}','{exerciseId}'), '{viewName}', '{exerciseName}')"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_applications_views_view_id",
                table: "applications");

            migrationBuilder.DropForeignKey(
                name: "FK_team_memberships_view_memberships_view_membership_id",
                table: "team_memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_teams_views_view_id",
                table: "teams");

            migrationBuilder.RenameColumn(
                name: "view_id",
                table: "teams",
                newName: "exercise_id");

            migrationBuilder.RenameIndex(
                name: "IX_teams_view_id",
                table: "teams",
                newName: "IX_teams_exercise_id");

            migrationBuilder.RenameColumn(
                name: "view_membership_id",
                table: "team_memberships",
                newName: "exercise_membership_id");

            migrationBuilder.RenameIndex(
                name: "IX_team_memberships_view_membership_id",
                table: "team_memberships",
                newName: "IX_team_memberships_exercise_membership_id");

            migrationBuilder.RenameColumn(
                name: "view_id",
                table: "notifications",
                newName: "exercise_id");

            migrationBuilder.RenameColumn(
                name: "view_id",
                table: "applications",
                newName: "exercise_id");

            migrationBuilder.RenameIndex(
                name: "IX_applications_view_id",
                table: "applications",
                newName: "IX_applications_exercise_id");

            migrationBuilder.RenameColumn(
                name: "view_id",
                table: "view_memberships",
                newName: "exercise_id");

            migrationBuilder.RenameTable(
                name: "views",
                newName: "exercises");

            migrationBuilder.RenameTable(
                name: "view_memberships",
                newName: "exercise_memberships");

            migrationBuilder.RenameIndex(
                name: "IX_view_memberships_primary_team_membership_id",
                table: "exercise_memberships",
                newName: "IX_exercise_memberships_primary_team_membership_id");

            migrationBuilder.RenameIndex(
                name: "IX_view_memberships_user_id",
                table: "exercise_memberships",
                newName: "IX_exercise_memberships_user_id");

            migrationBuilder.DropIndex(
                name: "IX_view_memberships_view_id_user_id",
                table: "exercise_memberships"
            );

            migrationBuilder.CreateIndex(
                name: "IX_exercise_memberships_exercise_id_user_id",
                table: "exercise_memberships",
                columns: new[] { "exercise_id", "user_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_applications_exercises_exercise_id",
                table: "applications",
                column: "exercise_id",
                principalTable: "exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_team_memberships_exercise_memberships_exercise_membership_id",
                table: "team_memberships",
                column: "exercise_membership_id",
                principalTable: "exercise_memberships",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_teams_exercises_exercise_id",
                table: "teams",
                column: "exercise_id",
                principalTable: "exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
