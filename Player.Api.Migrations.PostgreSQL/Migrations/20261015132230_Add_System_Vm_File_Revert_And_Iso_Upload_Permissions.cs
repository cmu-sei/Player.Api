// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Add_System_Vm_File_Revert_And_Iso_Upload_Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[,]
                {
                    { new Guid("28990394-5ad7-4371-ab21-4a4c2384e420"), "Allows reverting all Vms to a snapshot", false, "RevertVms" },
                    { new Guid("9f4d31aa-59ed-40a4-91fb-bccd74ab1ca6"), "Allows uploading files directly to all Vms", false, "UploadVmFiles" },
                    { new Guid("a39846e0-0edd-41f5-838f-7ef3c101f7c0"), "Allows downloading files directly from all Vms", false, "DownloadVmFiles" },
                    { new Guid("ea028d07-9dc1-4fd6-9296-43e90cb4484e"), "Allows uploading ISOs for any View or Team across all Views", false, "UploadIsos" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("28990394-5ad7-4371-ab21-4a4c2384e420"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("9f4d31aa-59ed-40a4-91fb-bccd74ab1ca6"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("a39846e0-0edd-41f5-838f-7ef3c101f7c0"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("ea028d07-9dc1-4fd6-9296-43e90cb4484e"));
        }
    }
}
