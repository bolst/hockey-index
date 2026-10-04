using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HockeyIndex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Platform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blocklist",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocklist", x => x.id);
                    table.CheckConstraint("ck_blocklist_kind", "kind IN ('phone','domain')");
                });

            migrationBuilder.CreateTable(
                name: "breakers",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    open_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    opened_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_breakers", x => x.name);
                    table.CheckConstraint("ck_breakers_opened_by", "opened_by IN ('auto','twilio_webhook','admin')");
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provider_usage",
                columns: table => new
                {
                    provider = table.Column<string>(type: "text", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    calls = table.Column<int>(type: "integer", nullable: false),
                    cap = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_usage", x => new { x.provider, x.host_id, x.day });
                });

            migrationBuilder.CreateTable(
                name: "url_verdicts",
                columns: table => new
                {
                    url_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    verdict = table.Column<string>(type: "text", nullable: false),
                    threat_types = table.Column<string[]>(type: "text[]", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_url_verdicts", x => x.url_hash);
                });

            migrationBuilder.CreateIndex(
                name: "ux_blocklist_kind_value",
                table: "blocklist",
                columns: new[] { "kind", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_url_verdicts_expires_at",
                table: "url_verdicts",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blocklist");

            migrationBuilder.DropTable(
                name: "breakers");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "provider_usage");

            migrationBuilder.DropTable(
                name: "url_verdicts");
        }
    }
}
