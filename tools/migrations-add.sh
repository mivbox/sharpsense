#!/usr/bin/env bash
set -euo pipefail

exec 3>&1 # keep near the start of the script
function say () {
  printf "%b\n" "[migrations] $1" >&3
}

function say_err() {
  if [ -t 1 ] && command -v tput > /dev/null; then
    RED='\033[0;31m'
    NC='\033[0m' # No Color
  fi
  printf "%b\n" "${RED:-}[migrations] Error: $1${NC:-}" >&2
}

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
pushd "$SCRIPT_DIR" > /dev/null

name="default"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --name=*) name="${1#*=}"; shift 1;;
    --name) say_err "$1 requires an argument" >&2; exit 1;;
    -*) say_err "unknown option: $1"; exit 1;;
    *) say_err "unknown argument: $1"; exit 1;;
  esac
done

projectDir="../src"
Host="${projectDir}/SharpSense.Infrastructure.MigrationsHost"
infrastructureProject="${projectDir}/SharpSense.Infrastructure"
dbContext="SharpSenseDbContext"

say "Adding ${name} to migrations for Storage"
say "Host: ${Host}"
say "Infrastructure Project: ${infrastructureProject}"
say "Db Context: ${dbContext}"

dotnet dotnet-ef migrations add "${name}" \
 --project "${infrastructureProject}" \
 --startup-project "${Host}" \
 --context "${dbContext}" \
 --output-dir "Persistence/Migrations" \
 --verbose \
 -- --provider Sqlite
