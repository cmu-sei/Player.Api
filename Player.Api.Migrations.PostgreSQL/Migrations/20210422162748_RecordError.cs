using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class RecordError : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_error",
                table: "webhooks",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_error",
                table: "webhooks");
        }
    }
}
