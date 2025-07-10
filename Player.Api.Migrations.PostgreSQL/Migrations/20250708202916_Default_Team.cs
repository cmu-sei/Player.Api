// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Default_Team : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_team_id",
                table: "views",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_views_default_team_id",
                table: "views",
                column: "default_team_id");

            migrationBuilder.AddForeignKey(
                name: "FK_views_teams_default_team_id",
                table: "views",
                column: "default_team_id",
                principalTable: "teams",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_views_teams_default_team_id",
                table: "views");

            migrationBuilder.DropIndex(
                name: "IX_views_default_team_id",
                table: "views");

            migrationBuilder.DropColumn(
                name: "default_team_id",
                table: "views");
        }
    }
}
