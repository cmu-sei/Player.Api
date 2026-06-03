using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Add_DateCreated_To_Views : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "date_created",
                table: "views",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "date_created",
                table: "views");
        }
    }
}
