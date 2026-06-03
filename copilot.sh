#!/bin/bash

REPO_ROOT=$(git rev-parse --show-toplevel)
cd "$REPO_ROOT" || exit 1

COPILOT_FLAGS=(
  "--allow-tool" "shell(dotnet:*)"
  "--allow-tool" "shell(git:status,git:diff,git:add,git:commit)"
  "--allow-tool" "sharpsense(semantic_search),sharpsense(trace_node),sharpsense(refactor_symbol),sharpsense(context),sharpsense(ctx_execute),sharpsense(get_inheritors)"
  "--deny-tool" "shell(git:push,rm)"
)

if [[ -f "mcp-config.json" ]]; then
  COPILOT_FLAGS+=("--additional-mcp-config" "@./mcp-config.json")
fi

if ! pgrep -x "ollama" > /dev/null; then
  echo "Starting Ollama server with 32k context..."
  OLLAMA_KV_CACHE_TYPE=q8_0 OLLAMA_CONTEXT_LENGTH=57344 ollama serve > /dev/null 2>&1 &

  # Give the daemon 3 seconds to initialize its internal networking
  sleep 3
else
  echo "Ollama server is already running."
fi

echo "Warming up the Qwen 35B model in memory"
ollama run qwen3.6:35b-mlx ""
export COPILOT_PROVIDER_BASE_URL="http://localhost:11434/v1"
export COPILOT_PROVIDER_API_KEY=""
export COPILOT_PROVIDER_WIRE_API="responses"
export COPILOT_MODEL="qwen3.6:35b-mlx"
export COPILOT_PROVIDER_MAX_PROMPT_TOKENS=57344
export COPILOT_PROVIDER_MAX_OUTPUT_TOKENS=8192

echo "Model loaded. Launching Copilot"
exec copilot "${COPILOT_FLAGS[@]}" "$@"
