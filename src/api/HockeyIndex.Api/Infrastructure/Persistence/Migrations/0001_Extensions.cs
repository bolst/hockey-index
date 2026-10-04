using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HockeyIndex.Api.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// postgis is not a trusted extension, so hi_migrator cannot create it; deploy/postgres/init/00-extensions.sql
    /// creates it as superuser. Fail loudly here when it is missing. The remaining extensions are trusted (PG 13+).
    /// </summary>
    public partial class Extensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'postgis') THEN
                        RAISE EXCEPTION 'postgis extension is not installed; run deploy/postgres/init/00-extensions.sql as superuser';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP EXTENSION IF EXISTS unaccent;
                DROP EXTENSION IF EXISTS citext;
                DROP EXTENSION IF EXISTS btree_gist;
                DROP EXTENSION IF EXISTS pg_trgm;
                """);
        }
    }
}
