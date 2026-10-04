#!/usr/bin/env bash
# Restore the newest (or a named) encrypted backup into a target Postgres and run sanity checks.
# Streams rclone -> age -> pg_restore; the plaintext dump never touches disk.
# Logs object names, byte sizes, and row counts only. No row data, no `set -x`.
#
# Target database (standard libpq variables; needs a superuser so extensions can be created):
#   PGHOST PGPORT PGUSER PGPASSWORD PGDATABASE
# Source (one of):
#   BACKUP_REMOTE            any rclone path, e.g. "r2:bucket" or "local:/backups"
#   R2_ENDPOINT R2_BUCKET R2_ACCESS_KEY_ID R2_SECRET_ACCESS_KEY   (builds the "r2" remote)
# Private key (one of; never written to disk by this script):
#   AGE_PRIVATE_KEY          the AGE-SECRET-KEY-... line
#   AGE_IDENTITY_FILE        path to an age identity file
# Optional:
#   RESTORE_OBJECT           object under daily/ (default: newest)
#   RESTORE_INIT             auto (default) | 1 | 0. auto runs deploy/postgres/init when postgis is absent
#   RESTORE_INIT_DIR         default: <repo>/deploy/postgres/init
#   RESTORE_MIGRATIONS_DIR   EF migrations folder; every applied migration MUST have a matching file
#   RESTORE_REQUIRE_MIGRATIONS  1 (default) fails when __EFMigrationsHistory is missing or empty
set -euo pipefail

log() { echo "restore: $*"; }
die() { echo "restore: $*" >&2; exit 1; }

: "${PGHOST:?}" "${PGUSER:?}" "${PGPASSWORD:?}" "${PGDATABASE:?}"
export PGHOST PGUSER PGPASSWORD PGDATABASE
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
init_dir=${RESTORE_INIT_DIR:-$script_dir/../postgres/init}

if [ -z "${BACKUP_REMOTE:-}" ]; then
  : "${R2_ENDPOINT:?}" "${R2_BUCKET:?}" "${R2_ACCESS_KEY_ID:?}" "${R2_SECRET_ACCESS_KEY:?}"
  export RCLONE_CONFIG_R2_TYPE=s3
  export RCLONE_CONFIG_R2_PROVIDER=Cloudflare
  export RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT"
  export RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID"
  export RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY"
  BACKUP_REMOTE="r2:${R2_BUCKET}"
fi
export RCLONE_LOG_LEVEL=${RCLONE_LOG_LEVEL:-ERROR}
remote=${BACKUP_REMOTE%/}

[ -n "${AGE_PRIVATE_KEY:-}" ] || [ -n "${AGE_IDENTITY_FILE:-}" ] \
  || die "set AGE_PRIVATE_KEY or AGE_IDENTITY_FILE"

psql_target() { psql --no-psqlrc --quiet --set ON_ERROR_STOP=1 "$@"; }

age_decrypt() {
  if [ -n "${AGE_IDENTITY_FILE:-}" ]; then
    age --decrypt --identity "$AGE_IDENTITY_FILE"
  else
    age --decrypt --identity <(printf '%s\n' "$AGE_PRIVATE_KEY")
  fi
}

object=${RESTORE_OBJECT:-}
if [ -z "$object" ]; then
  object=$(rclone lsf "${remote}/daily" --files-only | sort | tail -n 1)
fi
[ -n "$object" ] || die "no backups found under ${remote}/daily"
size_bytes=$(rclone size --json "${remote}/daily/${object}" | sed -n 's/.*"bytes":[[:space:]]*\([0-9]*\).*/\1/p')
log "object=${object} bytes=${size_bytes}"

postgis_present=$(psql_target -Atc "select count(*) from pg_extension where extname = 'postgis'")
case ${RESTORE_INIT:-auto} in
  1) run_init=1 ;;
  0) run_init=0 ;;
  auto) if [ "$postgis_present" = 0 ]; then run_init=1; else run_init=0; fi ;;
  *) die "RESTORE_INIT must be auto, 1, or 0" ;;
esac
if [ "$run_init" = 1 ]; then
  log "running init scripts from ${init_dir}"
  for script in "$init_dir"/*; do
    case $script in
      *.sql) psql_target --file "$script" >/dev/null ;;
      *.sh) POSTGRES_USER=$PGUSER POSTGRES_DB=$PGDATABASE bash "$script" >/dev/null ;;
    esac
  done
fi

log "restoring"
rclone cat "${remote}/daily/${object}" \
  | age_decrypt \
  | pg_restore --exit-on-error --no-owner --dbname "$PGDATABASE"
log "restore finished"

log "sanity checks"
test "$(psql_target -Atc "select count(*) from pg_extension where extname = 'postgis'")" -eq 1 \
  || die "postgis extension missing after restore"

history_exists=$(psql_target -Atc "select to_regclass('public.\"__EFMigrationsHistory\"') is not null")
if [ "$history_exists" = t ]; then
  applied=$(psql_target -Atc 'select count(*) from "__EFMigrationsHistory"')
  log "migrations applied: ${applied}"
  if [ -n "${RESTORE_MIGRATIONS_DIR:-}" ]; then
    while read -r id; do
      name=${id#*_}
      compgen -G "${RESTORE_MIGRATIONS_DIR}/*_${name}.cs" >/dev/null \
        || die "applied migration ${id} has no file in ${RESTORE_MIGRATIONS_DIR}"
    done < <(psql_target -Atc 'select "MigrationId" from "__EFMigrationsHistory" order by 1')
    log "every applied migration has a matching file"
  fi
  if [ "${RESTORE_REQUIRE_MIGRATIONS:-1}" = 1 ] && [ "$applied" -eq 0 ]; then
    die "__EFMigrationsHistory is empty"
  fi
elif [ "${RESTORE_REQUIRE_MIGRATIONS:-1}" = 1 ]; then
  die "__EFMigrationsHistory is missing"
fi

log "row counts per table (public schema)"
psql_target --tuples-only --no-align --field-separator ' ' <<'SQL'
select format('select %L as table, count(*) as rows from %I.%I', schemaname || '.' || tablename, schemaname, tablename)
from pg_tables
where schemaname = 'public' and tablename <> 'spatial_ref_sys'
order by tablename
\gexec
SQL
log "done"
