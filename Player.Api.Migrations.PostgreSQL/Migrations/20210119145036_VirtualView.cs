// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class VirtualView : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AlterColumn<Guid>(
                name: "view_id",
                table: "files",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_files_view_id",
                table: "files",
                column: "view_id");

            migrationBuilder.AddForeignKey(
                name: "FK_files_views_view_id",
                table: "files",
                column: "view_id",
                principalTable: "views",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_files_views_view_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_view_id",
                table: "files");

            migrationBuilder.AlterColumn<Guid>(
                name: "view_id",
                table: "files",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "view_entity_id",
                table: "files",
                type: "uuid",
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
    }
}
