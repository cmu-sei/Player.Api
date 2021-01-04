// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class Fixed_Membership_Relation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_exercise_memberships_team_memberships_primary_team_membersh~",
                table: "exercise_memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_team_memberships_exercise_memberships_exercise_membership_e~",
                table: "team_memberships");

            migrationBuilder.DropIndex(
                name: "IX_team_memberships_exercise_membership_entity_id",
                table: "team_memberships");

            migrationBuilder.DropIndex(
                name: "IX_exercise_memberships_primary_team_membership_id",
                table: "exercise_memberships");

            migrationBuilder.DropColumn(
                name: "exercise_membership_entity_id",
                table: "team_memberships");

            migrationBuilder.AlterColumn<Guid>(
                name: "primary_team_membership_id",
                table: "exercise_memberships",
                nullable: true,
                oldClrType: typeof(Guid));

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_exercise_membership_id",
                table: "team_memberships",
                column: "exercise_membership_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_memberships_primary_team_membership_id",
                table: "exercise_memberships",
                column: "primary_team_membership_id");

            migrationBuilder.AddForeignKey(
                name: "FK_exercise_memberships_team_memberships_primary_team_membersh~",
                table: "exercise_memberships",
                column: "primary_team_membership_id",
                principalTable: "team_memberships",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_team_memberships_exercise_memberships_exercise_membership_id",
                table: "team_memberships",
                column: "exercise_membership_id",
                principalTable: "exercise_memberships",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_exercise_memberships_team_memberships_primary_team_membersh~",
                table: "exercise_memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_team_memberships_exercise_memberships_exercise_membership_id",
                table: "team_memberships");

            migrationBuilder.DropIndex(
                name: "IX_team_memberships_exercise_membership_id",
                table: "team_memberships");

            migrationBuilder.DropIndex(
                name: "IX_exercise_memberships_primary_team_membership_id",
                table: "exercise_memberships");

            migrationBuilder.AddColumn<Guid>(
                name: "exercise_membership_entity_id",
                table: "team_memberships",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "primary_team_membership_id",
                table: "exercise_memberships",
                nullable: false,
                oldClrType: typeof(Guid),
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_exercise_membership_entity_id",
                table: "team_memberships",
                column: "exercise_membership_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_memberships_primary_team_membership_id",
                table: "exercise_memberships",
                column: "primary_team_membership_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_exercise_memberships_team_memberships_primary_team_membersh~",
                table: "exercise_memberships",
                column: "primary_team_membership_id",
                principalTable: "team_memberships",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_team_memberships_exercise_memberships_exercise_membership_e~",
                table: "team_memberships",
                column: "exercise_membership_entity_id",
                principalTable: "exercise_memberships",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
