// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class ViewFiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "view_entity_id",
                table: "files",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_view_entity_id",
                table: "files",
                column: "view_entity_id");

            migrationBuilder.AddForeignKey(
                name: "FK_files_views_view_entity_id",
                table: "files",
                column: "view_entity_id",
                principalTable: "views",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_files_views_view_entity_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_view_entity_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "view_entity_id",
                table: "files");
        }
    }
}
