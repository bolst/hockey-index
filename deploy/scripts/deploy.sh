#!/usr/bin/env bash
# Forced command target. SSH_ORIGINAL_COMMAND is "<env> <tag>" with env in staging|prod.
set -euo pipefail

readonly deploy_dir=${HI_DEPLOY_DIR:-/opt/hockey-index}
readonly state_dir=$deploy_dir/state
readonly gate_timeout_seconds=${HI_GATE_TIMEOUT:-180}
readonly tag_pattern='^sha-[0-9a-f]{12}$'

log() { echo "deploy: $*"; }
die() { echo "deploy: $*" >&2; exit 1; }

read -r target tag extra <<<"${SSH_ORIGINAL_COMMAND:-}" || true
[ -z "${extra:-}" ] || die "unexpected extra arguments"
[[ "${target:-}" =~ ^(staging|prod)$ ]] || die "environment must be staging or prod"
[[ "${tag:-}" =~ $tag_pattern ]] || die "tag must match $tag_pattern"

case $target in
  prod)
    project=hockey-index
    compose_file=$deploy_dir/compose.yaml
    tag_variable=HI_API_TAG
    api=api
    migrator=migrator
    database=pg-prod
    extra_services=(cloudflared backup)
    ;;
  staging)
    project=hockey-index-staging
    compose_file=$deploy_dir/compose.staging.yaml
    tag_variable=STAGING_API_TAG
    api=api-staging
    migrator=migrator-staging
    database=pg-staging
    extra_services=()
    ;;
esac

readonly state_file=$state_dir/$target.tag
mkdir -p "$state_dir"
exec 9>"$state_dir/deploy.lock"
flock -n 9 || die "another deploy is running"

compose() {
  docker compose --project-name "$project" --env-file "$deploy_dir/.env" -f "$compose_file" "$@"
}

health_gate() {
  local deadline=$((SECONDS + gate_timeout_seconds)) endpoint
  for endpoint in healthz readyz; do
    until compose exec -T "$api" sh -c "curl -fsS \"http://\$HI_OPS_BIND:8081/$endpoint\"" >/dev/null 2>&1; do
      if [ "$SECONDS" -ge "$deadline" ]; then
        log "health gate failed on /$endpoint"
        return 1
      fi
      sleep 3
    done
    log "/$endpoint ok"
  done
}

release() {
  export "$tag_variable=$1"
  compose pull "$api" "$migrator"
  compose up -d --wait "$database"
  if [ "${2:-migrate}" = migrate ]; then
    compose run --rm "$migrator"
  fi
  compose up -d "$api" "${extra_services[@]}"
  health_gate
}

if [ "$target" = staging ]; then
  docker network inspect hockey-index-edge >/dev/null 2>&1 \
    || die "network hockey-index-edge is missing: deploy prod first"
fi

previous=
if [ -f "$state_file" ]; then
  previous=$(<"$state_file")
fi

log "$target -> $tag (previous: ${previous:-none})"
if release "$tag"; then
  echo "$tag" >"$state_file"
  log "done"
  exit 0
fi

if [ -n "$previous" ] && [[ "$previous" =~ $tag_pattern ]]; then
  log "rolling back to $previous"
  if release "$previous" skip-migrate; then
    log "rollback ok"
  else
    log "rollback failed"
  fi
else
  log "no previous tag recorded, cannot roll back"
fi
exit 1
