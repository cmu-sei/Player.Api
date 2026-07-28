// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Add_Iso_Delete_Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[] { new Guid("0189a1b9-d975-4f14-b93c-db652f7dc064"), "Allows deleting any ISO across all Views", false, "DeleteIsos" });

            migrationBuilder.InsertData(
                table: "team_permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("37256f30-4a64-425d-86f1-e6302bedc702"), "Allows deleting ISOs that are available to any Teams in the View", false, "DeleteViewIsos" },
                    { new Guid("b7d05581-0ba4-442f-81d8-3da72ea6533e"), "Allows deleting ISOs that are available to members of the Team", false, "DeleteTeamIsos" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0189a1b9-d975-4f14-b93c-db652f7dc064"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("37256f30-4a64-425d-86f1-e6302bedc702"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("b7d05581-0ba4-442f-81d8-3da72ea6533e"));
        }
    }
}
