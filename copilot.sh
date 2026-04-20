#!/bin/bash

REPO_ROOT=$(git rev-parse --show-toplevel)
cd "$REPO_ROOT" || exit 1

COPILOT_FLAGS=(
  "--allow-tool" "shell(dotnet:*)"
  "--allow-tool" "shell(git:status,git:diff,git:add,git:commit)"
  "--deny-tool" "shell(git:push,rm)"
)

if [[ -f "mcp-config.json" ]]; then
  COPILOT_FLAGS+=("--additional-mcp-config" "@./mcp-config.json")
fi

exec copilot "${COPILOT_FLAGS[@]}" "$@"
