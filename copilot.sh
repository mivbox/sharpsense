#!/bin/bash

REPO_ROOT=$(git rev-parse --show-toplevel)
cd "$REPO_ROOT" || exit 1

# Ensure the OpenRouter API key is set in the environment
if [[ -z "${OPENROUTER_API_KEY}" ]]; then
  echo "Error: OPENROUTER_API_KEY environment variable is not set."
  echo "Please run: export OPENROUTER_API_KEY='your-api-key-here'"
  exit 1
fi

COPILOT_FLAGS=(
  "--allow-tool" "shell(dotnet:*)"
  "--allow-tool" "shell(git:status,git:diff,git:add,git:commit)"
  "--allow-tool" "sharpsense(semantic_search),sharpsense(trace_node),sharpsense(refactor_symbol),sharpsense(context),sharpsense(ctx_execute),sharpsense(get_inheritors),sharpsense(attach_memory),sharpsense(delete_memory),sharpsense(get_memory)"
  "--deny-tool" "shell(git:push,rm)"
)

if [[ -f "mcp-config.json" ]]; then
  COPILOT_FLAGS+=("--additional-mcp-config" "@./mcp-config.json")
fi


export COPILOT_PROVIDER_BASE_URL="https://openrouter.ai/api/v1"
export COPILOT_PROVIDER_API_KEY="${OPENROUTER_API_KEY}"
export COPILOT_PROVIDER_WIRE_API="responses"

# Set this to your desired OpenRouter model ID
export COPILOT_PROVIDER_MAX_PROMPT_TOKENS=1048576
export COPILOT_PROVIDER_MAX_OUTPUT_TOKENS=512000
export COPILOT_MODEL="minimax/minimax-m3"

echo "Launching Copilot connected to OpenRouter using model: $COPILOT_MODEL"
exec copilot "${COPILOT_FLAGS[@]}" "$@"
