using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace HockeyIndex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Events : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rink_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    starts_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ends_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    schedule_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    skill_range = table.Column<NpgsqlRange<int>>(type: "int4range", nullable: false),
                    fee_cents = table.Column<int>(type: "integer", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    join_instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    pending_join_instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    publish_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hidden_reason = table.Column<string>(type: "text", nullable: true),
                    hidden_from_status = table.Column<string>(type: "text", nullable: true),
                    notice = table.Column<string>(type: "text", nullable: true),
                    last_scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("ck_events_archived_at", "status <> 'archived' OR archived_at IS NOT NULL");
                    table.CheckConstraint("ck_events_currency", "currency IN ('CAD','USD')");
                    table.CheckConstraint("ck_events_fee_cents", "fee_cents IS NULL OR fee_cents BETWEEN 0 AND 100000");
                    table.CheckConstraint("ck_events_hidden_from_status", "hidden_from_status IS NULL OR hidden_from_status IN ('published','cancelled','archived')");
                    table.CheckConstraint("ck_events_hidden_from_status_required", "status <> 'hidden' OR hidden_from_status IS NOT NULL");
                    table.CheckConstraint("ck_events_hidden_reason", "hidden_reason IS NULL OR hidden_reason IN ('admin','reports','link_scan','domain_blocklist','host_banned')");
                    table.CheckConstraint("ck_events_hidden_reason_required", "status <> 'hidden' OR hidden_reason IS NOT NULL");
                    table.CheckConstraint("ck_events_hidden_reason_scope", "status IN ('hidden','archived') OR hidden_reason IS NULL");
                    table.CheckConstraint("ck_events_local_time_order", "ends_local > starts_local");
                    table.CheckConstraint("ck_events_notice", "notice IS NULL OR notice IN ('publish_blocked','publish_limit_reached','publish_scan_expired','join_instructions_blocked')");
                    table.CheckConstraint("ck_events_pending_join_not_draft", "pending_join_instructions IS NULL OR status <> 'draft'");
                    table.CheckConstraint("ck_events_public_id", "public_id ~ '^[a-z2-7]{10}$'");
                    table.CheckConstraint("ck_events_publish_requested", "publish_requested_at IS NULL OR status = 'draft'");
                    table.CheckConstraint("ck_events_schedule_text", "type <> 'scrimmage' OR schedule_text IS NULL");
                    table.CheckConstraint("ck_events_skill_range", "skill_range <@ int4range(0,7,'[]') AND NOT isempty(skill_range) AND NOT lower_inf(skill_range) AND NOT upper_inf(skill_range)");
                    table.CheckConstraint("ck_events_status", "status IN ('draft','published','cancelled','hidden','archived')");
                    table.CheckConstraint("ck_events_time_order", "ends_at > starts_at");
                    table.CheckConstraint("ck_events_type", "type IN ('scrimmage','league','tournament')");
                    table.ForeignKey(
                        name: "fk_events_asp_net_users_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_runs",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    rows_affected = table.Column<long>(type: "bigint", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_runs", x => x.job_name);
                });

            migrationBuilder.CreateTable(
                name: "event_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "text", nullable: false),
                    to_status = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_status_changes", x => x.id);
                    table.CheckConstraint("ck_event_status_changes_from_status", "from_status IN ('draft','published','cancelled','hidden','archived')");
                    table.CheckConstraint("ck_event_status_changes_to_status", "to_status IN ('draft','published','cancelled','hidden','archived')");
                    table.ForeignKey(
                        name: "fk_event_status_changes_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "link_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    final_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    final_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    redirect_hops = table.Column<int>(type: "integer", nullable: false),
                    verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    threat_types = table.Column<string[]>(type: "text[]", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_link_scans", x => x.id);
                    table.CheckConstraint("ck_link_scans_provider", "provider IN ('webrisk','blocklist','none')");
                    table.CheckConstraint("ck_link_scans_verdict", "verdict IN ('safe','malicious','unknown')");
                    table.ForeignKey(
                        name: "fk_link_scans_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_venues_public_id",
                table: "venues",
                sql: "public_id ~ '^[a-z2-7]{10}$'");

            migrationBuilder.CreateIndex(
                name: "ix_event_status_changes_event_id",
                table: "event_status_changes",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_status_changes_host_to_at",
                table: "event_status_changes",
                columns: new[] { "host_id", "to_status", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_events_host_status",
                table: "events",
                columns: new[] { "host_id", "status", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_events_pending_ji",
                table: "events",
                column: "id",
                filter: "pending_join_instructions IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_events_publish_requested",
                table: "events",
                column: "publish_requested_at",
                filter: "publish_requested_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_events_skill",
                table: "events",
                column: "skill_range",
                filter: "status IN ('published','cancelled')")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_events_status_ends",
                table: "events",
                columns: new[] { "status", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_events_venue_id",
                table: "events",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "ux_events_public_id",
                table: "events",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_link_scans_event_checked",
                table: "link_scans",
                columns: new[] { "event_id", "checked_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_link_scans_final_host",
                table: "link_scans",
                column: "final_host");

            migrationBuilder.Sql(
                "CREATE INDEX ix_events_time_active ON events USING gist (tstzrange(starts_at, ends_at, '[)')) WHERE status IN ('published','cancelled');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_events_time_active;");

            migrationBuilder.DropTable(
                name: "event_status_changes");

            migrationBuilder.DropTable(
                name: "job_runs");

            migrationBuilder.DropTable(
                name: "link_scans");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venues_public_id",
                table: "venues");
        }
    }
}
