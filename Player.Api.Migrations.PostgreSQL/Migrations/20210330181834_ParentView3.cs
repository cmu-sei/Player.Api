/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class ParentView3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_views_views_view_id",
                table: "views");

            migrationBuilder.AlterColumn<Guid>(
                name: "view_id",
                table: "views",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_views_views_view_id",
                table: "views",
                column: "view_id",
                principalTable: "views",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_views_views_view_id",
                table: "views");

            migrationBuilder.AlterColumn<Guid>(
                name: "view_id",
                table: "views",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_views_views_view_id",
                table: "views",
                column: "view_id",
                principalTable: "views",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
