import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Memory, MemoryIntent, MemoryMetadata } from "../types/memory";

const API_BASE = "/api/memory";

type AddMemoryPayload = {
  content: string;
  tags?: string[];
  intent?: MemoryIntent;
};

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    ...init,
    headers: { "content-type": "application/json", ...(init?.headers ?? {}) }
  });
  if (!response.ok) {
    let detail = response.statusText;
    try {
      const body = (await response.json()) as { error?: string };
      if (body.error) {
        detail = body.error;
      }
    } catch {
      // ignore — statusText is the best we have
    }

    throw new Error(detail);
  }

  return (await response.json()) as T;
}

export function useNodeMemories(nodeId: number | null, intents?: MemoryIntent[]) {
  const params = intents && intents.length > 0 ? `?intents=${intents.join(",")}` : "";
  return useQuery<MemoryMetadata[]>({
    queryKey: ["memory", "node", nodeId, intents ?? []],
    queryFn: () => fetchJson<MemoryMetadata[]>(`/api/memory/node/${nodeId}${params}`),
    enabled: nodeId !== null && nodeId > 0
  });
}

export function useMemory(memoryId: string | null) {
  return useQuery<Memory>({
    queryKey: ["memory", "item", memoryId],
    queryFn: () => fetchJson<Memory>(`${API_BASE}/${memoryId}`),
    enabled: memoryId !== null
  });
}

export function useAddMemory(nodeId: number) {
  const queryClient = useQueryClient();
  return useMutation<{ ok: boolean; intent: MemoryIntent }, Error, AddMemoryPayload>({
    mutationFn: (payload) =>
      fetchJson(`/api/memory/node/${nodeId}`, {
        method: "POST",
        body: JSON.stringify(payload)
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["memory", "node", nodeId] });
    }
  });
}

export function useDeleteMemory(nodeId: number) {
  const queryClient = useQueryClient();
  return useMutation<{ ok: boolean; memoryId: string }, Error, string>({
    mutationFn: (memoryId) =>
      fetchJson(`${API_BASE}/${memoryId}`, { method: "DELETE" }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["memory", "node", nodeId] });
      void queryClient.invalidateQueries({ queryKey: ["memory", "item"] });
    }
  });
}
