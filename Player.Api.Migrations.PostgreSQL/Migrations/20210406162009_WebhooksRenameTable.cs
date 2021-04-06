using Microsoft.EntityFrameworkCore.Migrations;

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    public partial class WebhooksRenameTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_webhook_subscription_event_type_entity_webhooks_subscriptio~",
                table: "webhook_subscription_event_type_entity");

            migrationBuilder.DropPrimaryKey(
                name: "PK_webhook_subscription_event_type_entity",
                table: "webhook_subscription_event_type_entity");

            migrationBuilder.RenameTable(
                name: "webhook_subscription_event_type_entity",
                newName: "webhook_subscription_event_types");

            migrationBuilder.RenameIndex(
                name: "IX_webhook_subscription_event_type_entity_subscription_id_even~",
                table: "webhook_subscription_event_types",
                newName: "IX_webhook_subscription_event_types_subscription_id_event_type");

            migrationBuilder.AddPrimaryKey(
                name: "PK_webhook_subscription_event_types",
                table: "webhook_subscription_event_types",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_webhook_subscription_event_types_webhooks_subscription_id",
                table: "webhook_subscription_event_types",
                column: "subscription_id",
                principalTable: "webhooks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_webhook_subscription_event_types_webhooks_subscription_id",
                table: "webhook_subscription_event_types");

            migrationBuilder.DropPrimaryKey(
                name: "PK_webhook_subscription_event_types",
                table: "webhook_subscription_event_types");

            migrationBuilder.RenameTable(
                name: "webhook_subscription_event_types",
                newName: "webhook_subscription_event_type_entity");

            migrationBuilder.RenameIndex(
                name: "IX_webhook_subscription_event_types_subscription_id_event_type",
                table: "webhook_subscription_event_type_entity",
                newName: "IX_webhook_subscription_event_type_entity_subscription_id_even~");

            migrationBuilder.AddPrimaryKey(
                name: "PK_webhook_subscription_event_type_entity",
                table: "webhook_subscription_event_type_entity",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_webhook_subscription_event_type_entity_webhooks_subscriptio~",
                table: "webhook_subscription_event_type_entity",
                column: "subscription_id",
                principalTable: "webhooks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
