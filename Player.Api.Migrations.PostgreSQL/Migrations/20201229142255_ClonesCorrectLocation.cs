﻿using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class ClonesCorrectLocation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clones",
                table: "files");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "clones",
                table: "views",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clones",
                table: "views");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "clones",
                table: "files",
                type: "uuid[]",
                nullable: true);
        }
    }
}