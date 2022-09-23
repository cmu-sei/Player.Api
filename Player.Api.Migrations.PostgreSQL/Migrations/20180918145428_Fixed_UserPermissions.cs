// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class Fixed_UserPermissions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_permissions_users_user_key",
                table: "user_permissions");

            migrationBuilder.DropIndex(
                name: "IX_user_permissions_user_key",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "user_key",
                table: "user_permissions");

            migrationBuilder.AddForeignKey(
                name: "FK_user_permissions_users_user_id",
                table: "user_permissions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_permissions_users_user_id",
                table: "user_permissions");

            migrationBuilder.AddColumn<int>(
                name: "user_key",
                table: "user_permissions",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_permissions_user_key",
                table: "user_permissions",
                column: "user_key");

            migrationBuilder.AddForeignKey(
                name: "FK_user_permissions_users_user_key",
                table: "user_permissions",
                column: "user_key",
                principalTable: "users",
                principalColumn: "key",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
