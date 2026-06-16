using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Player.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Add_TeamPermissionScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "team_permission_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_team_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_permission_scopes", x => x.id);
                    table.ForeignKey(
                        name: "FK_team_permission_scopes_teams_target_team_id",
                        column: x => x.target_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_team_permission_scopes_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_team_permission_scopes_target_team_id",
                table: "team_permission_scopes",
                column: "target_team_id");

            migrationBuilder.CreateIndex(
                name: "IX_team_permission_scopes_team_id_target_team_id",
                table: "team_permission_scopes",
                columns: new[] { "team_id", "target_team_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_permission_scopes");
        }
    }
}
