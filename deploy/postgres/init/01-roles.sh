#!/usr/bin/env bash
# Runs once as the superuser on a fresh data volume (docker-entrypoint-initdb.d), after 00-extensions.sql.
#   hi_migrator: owns the database and schema objects; runs migrations.
#   hi_app:      the API's role; DML only on tables hi_migrator creates.
# Passwords come from HI_MIGRATOR_PASSWORD / HI_APP_PASSWORD. The dev defaults exist only so local
# compose and Testcontainers work without extra setup; production compose requires both variables.
# No `set -u`: the postgres entrypoint sources non-executable init scripts into its own shell.

psql -v ON_ERROR_STOP=1 \
    --username "${POSTGRES_USER:-postgres}" \
    --dbname "${POSTGRES_DB:-postgres}" \
    --set migrator_password="${HI_MIGRATOR_PASSWORD:-dev-migrator}" \
    --set app_password="${HI_APP_PASSWORD:-dev-app}" <<'SQL'
SELECT format('CREATE ROLE hi_migrator LOGIN PASSWORD %L', :'migrator_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hi_migrator') \gexec
SELECT format('CREATE ROLE hi_app LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hi_app') \gexec

SELECT format('ALTER DATABASE %I OWNER TO hi_migrator', current_database()) \gexec
ALTER SCHEMA public OWNER TO hi_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

SELECT format('GRANT CONNECT ON DATABASE %I TO hi_app', current_database()) \gexec
GRANT USAGE ON SCHEMA public TO hi_app;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO hi_app;

ALTER DEFAULT PRIVILEGES FOR ROLE hi_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hi_app;
ALTER DEFAULT PRIVILEGES FOR ROLE hi_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO hi_app;
ALTER DEFAULT PRIVILEGES FOR ROLE hi_migrator IN SCHEMA public
    GRANT EXECUTE ON FUNCTIONS TO hi_app;
SQL
