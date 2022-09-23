// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class Webhooks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_view_id",
                table: "views",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    callback_uri = table.Column<string>(type: "text", nullable: true),
                    client_id = table.Column<string>(type: "text", nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhooks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_pending_events_webhooks_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "webhooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscription_event_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_subscription_event_types", x => x.id);
                    table.ForeignKey(
                        name: "FK_webhook_subscription_event_types_webhooks_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "webhooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_views_parent_view_id",
                table: "views",
                column: "parent_view_id");

            migrationBuilder.CreateIndex(
                name: "IX_pending_events_subscription_id",
                table: "pending_events",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_subscription_event_types_subscription_id_event_type",
                table: "webhook_subscription_event_types",
                columns: new[] { "subscription_id", "event_type" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_views_views_parent_view_id",
                table: "views",
                column: "parent_view_id",
                principalTable: "views",
                principalColumn: "id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_views_views_parent_view_id",
                table: "views");

            migrationBuilder.DropTable(
                name: "pending_events");

            migrationBuilder.DropTable(
                name: "webhook_subscription_event_types");

            migrationBuilder.DropTable(
                name: "webhooks");

            migrationBuilder.DropIndex(
                name: "IX_views_parent_view_id",
                table: "views");

            migrationBuilder.DropColumn(
                name: "parent_view_id",
                table: "views");
        }
    }
}
