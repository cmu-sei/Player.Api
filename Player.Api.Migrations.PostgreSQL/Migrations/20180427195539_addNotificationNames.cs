// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class addNotificationNames : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "from_name",
                table: "notifications",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "to_name",
                table: "notifications",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "from_name",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "to_name",
                table: "notifications");
        }
    }
}
