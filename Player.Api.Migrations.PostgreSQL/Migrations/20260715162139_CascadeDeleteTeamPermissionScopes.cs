// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class CascadeDeleteTeamPermissionScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_team_permission_scopes_teams_target_team_id",
                table: "team_permission_scopes");

            migrationBuilder.DropForeignKey(
                name: "FK_team_permission_scopes_teams_team_id",
                table: "team_permission_scopes");

            migrationBuilder.AddForeignKey(
                name: "FK_team_permission_scopes_teams_target_team_id",
                table: "team_permission_scopes",
                column: "target_team_id",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_team_permission_scopes_teams_team_id",
                table: "team_permission_scopes",
                column: "team_id",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_team_permission_scopes_teams_target_team_id",
                table: "team_permission_scopes");

            migrationBuilder.DropForeignKey(
                name: "FK_team_permission_scopes_teams_team_id",
                table: "team_permission_scopes");

            migrationBuilder.AddForeignKey(
                name: "FK_team_permission_scopes_teams_target_team_id",
                table: "team_permission_scopes",
                column: "target_team_id",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_team_permission_scopes_teams_team_id",
                table: "team_permission_scopes",
                column: "team_id",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
