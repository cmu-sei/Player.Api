using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class ParentView : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clones",
                table: "views");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_view_id",
                table: "views",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_views_parent_view_id",
                table: "views",
                column: "parent_view_id");

            migrationBuilder.AddForeignKey(
                name: "FK_views_views_parent_view_id",
                table: "views",
                column: "parent_view_id",
                principalTable: "views",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_views_views_parent_view_id",
                table: "views");

            migrationBuilder.DropIndex(
                name: "IX_views_parent_view_id",
                table: "views");

            migrationBuilder.DropColumn(
                name: "parent_view_id",
                table: "views");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "clones",
                table: "views",
                type: "uuid[]",
                nullable: true);
        }
    }
}
