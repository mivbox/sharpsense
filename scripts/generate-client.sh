#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ui_root="$repository_root/src/SharpSense.UI"
description_path="$ui_root/openapi/sharpsense.json"

case "${1:-}" in
  "") ;;
  --refresh)
    # Start the global `sharpsense ui` host first, or provide another running API's base URL.
    api_url="${SHARPSENSE_API_URL:-http://localhost:50069}"
    mkdir -p "$(dirname "$description_path")"
    temporary_description="$(mktemp "${description_path}.raw.XXXXXX")"
    normalized_description="$(mktemp "${description_path}.normalized.XXXXXX")"
    trap 'rm -f -- "$temporary_description" "$normalized_description"' EXIT
    curl --fail --silent --show-error --max-time 30 \
      "${api_url%/}/openapi/v1.json" >"$temporary_description"
    jq -eS 'if (.openapi | type) == "string" and (.paths | type) == "object" then .servers = [{"url": "/"}] else error("Invalid OpenAPI document") end' \
      "$temporary_description" >"$normalized_description"
    mv "$normalized_description" "$description_path"
    ;;
  --help|-h)
    printf 'Usage: scripts/generate-client.sh [--refresh]\n\n'
    printf 'Generate the TypeScript client from the checked-in OpenAPI contract.\n'
    printf 'Use --refresh to fetch the contract from the running UI server first.\n'
    printf 'SHARPSENSE_API_URL overrides http://localhost:50069.\n'
    exit 0
    ;;
  *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
esac

cd "$repository_root"
dotnet tool restore
dotnet tool run kiota generate \
  --language TypeScript \
  --openapi "$description_path" \
  --class-name SharpSenseClient \
  --output "$ui_root/src/shared/api/generated" \
  --clean-output \
  --exclude-backward-compatible

# Kiota emits whitespace-only lines; normalize them as part of generation.
node --input-type=module - "$ui_root/src/shared/api/generated" <<'JS'
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
function normalize(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) normalize(path);
    else if (entry.name.endsWith(".ts")) {
      const source = readFileSync(path, "utf8");
      writeFileSync(path, source.replace(/[ \t]+$/gm, ""));
    }
  }
}
normalize(process.argv[2]);
JS
