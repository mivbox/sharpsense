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

projectDir="../src"
Host="${projectDir}/SharpSense.Infrastructure.MigrationsHost"
infrastructureProject="${projectDir}/SharpSense.Infrastructure"
dbContext="SharpSenseDbContext"

say "Host: ${Host}"
say "Infrastructure Project: ${infrastructureProject}"
say "Db Context: ${dbContext}"

dotnet dotnet-ef migrations remove \
        --force \
        --project "${infrastructureProject}" \
        --startup-project "${Host}" \
        --context "${dbContext}" \
        --verbose -- --provider Sqlite
