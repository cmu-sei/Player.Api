using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class RevertVms_Permission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "team_permissions",
                columns: new[] { "id", "description", "immutable", "name" },
                values: new object[] { new Guid("42da22ae-ca0f-440f-87e0-5742799f60e1"), "Allows reverting a Vm to it's current snapshot", false, "RevertVms" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "team_permissions",
                keyColumn: "id",
                keyValue: new Guid("42da22ae-ca0f-440f-87e0-5742799f60e1"));
        }
    }
}
