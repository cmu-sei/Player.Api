// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class addNotificationExerciseId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "severity",
                table: "notifications");

            migrationBuilder.AddColumn<Guid>(
                name: "exercise_id",
                table: "notifications",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "priority",
                table: "notifications",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exercise_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "notifications");

            migrationBuilder.AddColumn<int>(
                name: "severity",
                table: "notifications",
                nullable: false,
                defaultValue: 0);
        }
    }
}
