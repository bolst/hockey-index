using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace HockeyIndex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Venues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "venues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    address_line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    city = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    region = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    geo = table.Column<Point>(type: "geography(Point,4326)", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_place_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    merged_into_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venues", x => x.id);
                    table.CheckConstraint("ck_venues_country", "country IN ('US','CA')");
                    table.CheckConstraint("ck_venues_provider", "provider IN ('mapbox','geocodio','esri','osm','manual')");
                    table.ForeignKey(
                        name: "fk_venues_hosts_created_by",
                        column: x => x.created_by,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_venues_venues_merged_into_id",
                        column: x => x.merged_into_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_venues_created_by",
                table: "venues",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_venues_geo",
                table: "venues",
                column: "geo")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_venues_merged_into_id",
                table: "venues",
                column: "merged_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_venues_name_trgm",
                table: "venues",
                column: "name_normalized")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ux_venues_provider_place",
                table: "venues",
                columns: new[] { "provider", "provider_place_id" },
                unique: true,
                filter: "provider_place_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_venues_public_id",
                table: "venues",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "venues");
        }
    }
}
