#!/bin/bash

REPO_ROOT=$(git rev-parse --show-toplevel)
cd "$REPO_ROOT" || exit 1

COPILOT_FLAGS=(
  "--allow-tool" "shell(dotnet:*)"
  "--allow-tool" "shell(git:status,git:diff,git:add,git:commit)"
  "--allow-tool" "sharpsense(semantic_search),sharpsense(trace_node)"
  "--deny-tool" "shell(git:push,rm)"
  "--deny-tool" "shell(grep,find,rg,ag)"
  "--deny-tool" "search_files"
)

if [[ -f "mcp-config.json" ]]; then
  COPILOT_FLAGS+=("--additional-mcp-config" "@./mcp-config.json")
fi

exec copilot "${COPILOT_FLAGS[@]}" "$@"
