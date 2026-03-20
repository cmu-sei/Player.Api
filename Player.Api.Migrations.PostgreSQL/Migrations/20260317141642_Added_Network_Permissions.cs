// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Added_Network_Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("06da87d8-7636-4a50-929a-bbff2fbad548"),
                column: "description",
                value: "Allows viewing all Views in the system");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("06e2699d-21a9-4053-922a-411499b3e923"),
                column: "description",
                value: "Allows creating new Views");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0ef125ff-c493-476d-a041-0b6af54f4d36"),
                column: "description",
                value: "Allows viewing Applications and ApplicationTemplates across all Views");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5597959a-ff30-4b73-9122-f21d17c19382"),
                column: "description",
                value: "Allows full management of Views, including modifying and deleting any View in the system");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5cc1e12e-7440-4bd8-9a54-ccb5bb0f3f1e"),
                column: "description",
                value: "Allows creating and managing User accounts");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("6c407f81-ab2e-4b24-911b-78b7f424b965"),
                column: "description",
                value: "Allows editing View properties across the system");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("70f3e368-7e7a-4166-9698-5c96dbb19ceb"),
                column: "description",
                value: "Allows viewing all Users in the system");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("8dc72622-565d-4b86-b6d7-1692dc803815"),
                column: "description",
                value: "Allows creating and managing system ApplicationTemplates and Applications within Views");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cfcc8ac3-6591-41b8-abe1-0456616b3798"),
                column: "description",
                value: "Allows viewing all Roles in the system");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("e15b0177-5250-4886-b062-4029a9371a99"),
                column: "description",
                value: "Allows viewing all Webhook Subscriptions in the system");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("e1772ce2-eacb-478f-bac8-2e77d49c608a"),
                column: "description",
                value: "Allows creating and managing Webhook Subscriptions");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f1416f76-aa64-4edc-bfa8-6f234da85060"),
                column: "description",
                value: "Allows creating and managing system Roles");

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("0c812be8-5a2a-4e61-b475-802efcc10fa2"), "Allows creating, updating, and deleting Network access across all Views", false, "ManageNetworks" },
                    { new Guid("df487ab3-e4d2-4879-8ba6-be626b5df5bc"), "Allows viewing Network access across all Views", false, "ViewNetworks" }
                });

            migrationBuilder.UpdateData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("42da22ae-ca0f-440f-87e0-5742799f60e1"),
                column: "description",
                value: "Allows reverting a Vm to a snapshot");

            migrationBuilder.UpdateData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("5ae96619-b40b-4fdb-bbef-ad476c21553d"),
                column: "description",
                value: "Allows editing all basic resources in the View, including making changes within Virtual Machines, if applicable");

            migrationBuilder.UpdateData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("83e41563-8b7f-4f43-b9d0-2d8dc12fc0bf"),
                column: "description",
                value: "Allows managing all Team resources, including adding and removing Users");

            migrationBuilder.UpdateData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"),
                column: "description",
                value: "Allows editing basic Team resources, including making changes within Virtual Machines, if applicable");

            migrationBuilder.InsertData(
                table: "team_permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("43aee79a-a73a-4aa9-8928-256c86d82f6d"), "Allows creating, updating, and deleting Network access for the View", false, "ManageNetworks" },
                    { new Guid("fbe821b7-4e5c-46f8-8c90-13fef17a680c"), "Allows viewing Network access for the View", false, "ViewNetworks" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0c812be8-5a2a-4e61-b475-802efcc10fa2"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("df487ab3-e4d2-4879-8ba6-be626b5df5bc"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("43aee79a-a73a-4aa9-8928-256c86d82f6d"));

            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("fbe821b7-4e5c-46f8-8c90-13fef17a680c"));

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("06da87d8-7636-4a50-929a-bbff2fbad548"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("06e2699d-21a9-4053-922a-411499b3e923"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0ef125ff-c493-476d-a041-0b6af54f4d36"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5597959a-ff30-4b73-9122-f21d17c19382"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5cc1e12e-7440-4bd8-9a54-ccb5bb0f3f1e"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("6c407f81-ab2e-4b24-911b-78b7f424b965"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("70f3e368-7e7a-4166-9698-5c96dbb19ceb"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("8dc72622-565d-4b86-b6d7-1692dc803815"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cfcc8ac3-6591-41b8-abe1-0456616b3798"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("e15b0177-5250-4886-b062-4029a9371a99"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("e1772ce2-eacb-478f-bac8-2e77d49c608a"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f1416f76-aa64-4edc-bfa8-6f234da85060"),
                column: "description",
                value: "");

            migrationBuilder.UpdateData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("42da22ae-ca0f-440f-87e0-5742799f60e1"),
                column: "description",
                value: "Allows reverting a Vm to it's current snapshot");

            migrationBuilder.UpdateData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("5ae96619-b40b-4fdb-bbef-ad476c21553d"),
                column: "description",
                value: "Allows editing all basic resources in the View, including making changes within Virtual Machines, if applicable.");

            migrationBuilder.UpdateData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("83e41563-8b7f-4f43-b9d0-2d8dc12fc0bf"),
                column: "description",
                value: "Allows managing all Team resources, including adding and removing Users.");

            migrationBuilder.UpdateData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("fbabccc8-48c7-478a-bc30-d4bd8950e3d5"),
                column: "description",
                value: "Allows editing basic Team resources, including making changes within Virtual Machines, if applicable.");
        }
    }
}
