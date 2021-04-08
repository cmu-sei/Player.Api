using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class PendingEventTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pending_events",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    event_type = table.Column<int>(nullable: false),
                    timestamp = table.Column<DateTime>(nullable: false),
                    effected_entity_id = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_events", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_events");
        }
    }
}
