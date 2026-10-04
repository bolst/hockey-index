#!/usr/bin/env bash
# Usage: check-no-ports.sh [prod-compose staging-compose cloudflared-config]
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
deploy_dir=$(dirname "$script_dir")
prod_file=${1:-$deploy_dir/compose.yaml}
staging_file=${2:-$deploy_dir/compose.staging.yaml}
tunnel_config=${3:-$deploy_dir/cloudflared/config.yml}
env_file=${COMPOSE_ENV_FILE:-$deploy_dir/.env.example}

command -v jq >/dev/null || { echo "missing dependency: jq" >&2; exit 2; }
if docker compose version >/dev/null 2>&1; then
  compose=(docker compose)
elif command -v docker-compose >/dev/null; then
  compose=(docker-compose)
else
  echo "missing dependency: docker compose" >&2
  exit 2
fi

failures=0
fail() {
  echo "FAIL: $*" >&2
  failures=$((failures + 1))
}

check_compose_file() {
  local file=$1 config
  config=$("${compose[@]}" --project-directory "$deploy_dir" --env-file "$env_file" -f "$file" config --format json)

  while IFS= read -r service; do
    fail "$file: service '$service' declares ports"
  done < <(jq -r '.services | to_entries[] | select((.value.ports // []) | length > 0) | .key' <<<"$config")

  while IFS= read -r service; do
    fail "$file: database service '$service' joins the edge network"
  done < <(jq -r '
    . as $root
    | .services | to_entries[]
    | select(.key | startswith("pg-"))
    | select((.value.networks // {}) | keys
        | any(. as $net | $net == "edge" or ($root.networks[$net].name // "") == "hockey-index-edge"))
    | .key' <<<"$config")
}

check_compose_file "$prod_file"
check_compose_file "$staging_file"

if grep -q '8081' "$tunnel_config"; then
  fail "$tunnel_config: cloudflared ingress mentions 8081"
fi

if [ "$failures" -gt 0 ]; then
  echo "check-no-ports: $failures violation(s)" >&2
  exit 1
fi
echo "check-no-ports: ok"
