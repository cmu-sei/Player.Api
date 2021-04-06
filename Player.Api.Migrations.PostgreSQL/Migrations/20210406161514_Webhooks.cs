using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class Webhooks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(nullable: true),
                    callback_uri = table.Column<string>(nullable: true),
                    client_id = table.Column<string>(nullable: true),
                    client_secret = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhooks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscription_event_type_entity",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    event_type = table.Column<int>(nullable: false),
                    subscription_id = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_subscription_event_type_entity", x => x.id);
                    table.ForeignKey(
                        name: "FK_webhook_subscription_event_type_entity_webhooks_subscriptio~",
                        column: x => x.subscription_id,
                        principalTable: "webhooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_subscription_event_type_entity_subscription_id_even~",
                table: "webhook_subscription_event_type_entity",
                columns: new[] { "subscription_id", "event_type" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_subscription_event_type_entity");

            migrationBuilder.DropTable(
                name: "webhooks");
        }
    }
}
