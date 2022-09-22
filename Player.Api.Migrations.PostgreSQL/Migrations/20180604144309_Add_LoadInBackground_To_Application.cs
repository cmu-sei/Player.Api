// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class Add_LoadInBackground_To_Application : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "load_in_background",
                table: "applications",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "load_in_background",
                table: "application_templates",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "load_in_background",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "load_in_background",
                table: "application_templates");
        }
    }
}
