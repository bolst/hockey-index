-- Runs once as the superuser on a fresh data volume (docker-entrypoint-initdb.d), in POSTGRES_DB.
-- postgis is not a trusted extension, so the migrator role cannot create it; migration 0001 asserts it exists.
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS unaccent;
