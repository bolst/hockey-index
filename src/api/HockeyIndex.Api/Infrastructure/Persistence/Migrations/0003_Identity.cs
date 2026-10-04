using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HockeyIndex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hosts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    phone_line_type = table.Column<string>(type: "text", nullable: true),
                    is_voip = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    banned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: false),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hosts", x => x.id);
                    table.CheckConstraint("ck_hosts_status", "status IN ('active','banned')");
                });

            migrationBuilder.CreateTable(
                name: "otp_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    target_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    ip_key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    npa_nxx = table.Column<string>(type: "char(6)", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    line_type = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_log", x => x.id);
                    table.CheckConstraint("ck_otp_log_channel", "channel IN ('sms','email')");
                    table.CheckConstraint("ck_otp_log_kind", "kind IN ('send','verify')");
                    table.CheckConstraint("ck_otp_log_outcome", "outcome IN ('sent','verified','failed','limited','rejected_region','rejected_blocklist','rejected_banned','rejected_line_type','turnstile_failed','breaker_open','budget_exhausted','invite_required')");
                });

            migrationBuilder.CreateTable(
                name: "signup_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_e164 = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signup_invites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "host_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_host_claims_hosts_user_id",
                        column: x => x.user_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "host_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_host_logins_hosts_user_id",
                        column: x => x.user_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "host_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_host_tokens_hosts_user_id",
                        column: x => x.user_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "login_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    link_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<short>(type: "smallint", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_tokens", x => x.id);
                    table.CheckConstraint("ck_login_tokens_attempts", "attempts BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_login_tokens_purpose", "purpose IN ('email_confirm','email_login','recycle_check')");
                    table.ForeignKey(
                        name: "fk_login_tokens_hosts_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_host_claims_user_id",
                table: "host_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_host_logins_user_id",
                table: "host_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_hosts_normalized_email",
                table: "hosts",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ux_hosts_normalized_user_name",
                table: "hosts",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_hosts_phone_number",
                table: "hosts",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_hosts_verified_email",
                table: "hosts",
                column: "normalized_email",
                unique: true,
                filter: "email_confirmed");

            migrationBuilder.CreateIndex(
                name: "ix_login_tokens_created_at",
                table: "login_tokens",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_login_tokens_host_purpose",
                table: "login_tokens",
                columns: new[] { "host_id", "purpose", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_login_tokens_link_hash",
                table: "login_tokens",
                column: "link_hash",
                unique: true,
                filter: "link_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_otp_log_created_at",
                table: "otp_log",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_otp_log_ip_created",
                table: "otp_log",
                columns: new[] { "ip_key_hash", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_otp_log_npa_nxx_created",
                table: "otp_log",
                columns: new[] { "npa_nxx", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_otp_log_target_created",
                table: "otp_log",
                columns: new[] { "target_hash", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_signup_invites_phone_e164",
                table: "signup_invites",
                column: "phone_e164");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_claims");

            migrationBuilder.DropTable(
                name: "host_logins");

            migrationBuilder.DropTable(
                name: "host_tokens");

            migrationBuilder.DropTable(
                name: "login_tokens");

            migrationBuilder.DropTable(
                name: "otp_log");

            migrationBuilder.DropTable(
                name: "signup_invites");

            migrationBuilder.DropTable(
                name: "hosts");
        }
    }
}
