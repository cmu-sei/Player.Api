// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Added_Network_Permissions2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "team_role_permissions",
                columns: new[] { "id", "permission_id", "role_id" },
                values: new object[] { new Guid("73c34ba3-bb8c-4c61-860c-e535be7d69b1"), new Guid("fbe821b7-4e5c-46f8-8c90-13fef17a680c"), new Guid("c875dcce-2488-4e73-8585-8375b4730151") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "team_role_permissions",
                keyColumn: "id",
                keyValue: new Guid("73c34ba3-bb8c-4c61-860c-e535be7d69b1"));
        }
    }
}
