import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useWorkspaceApi } from "../../shared/workspace/context";
import type { MemoryIntent } from "../../shared/api/models";

export function useNodeMemories(nodeId: number) {
  const { getNodeMemories } = useWorkspaceApi();
  return useQuery({
    queryKey: ["memories", "node", nodeId],
    queryFn: ({ signal }) => getNodeMemories(nodeId, signal),
    enabled: Number.isInteger(nodeId) && nodeId > 0,
  });
}
export function useMemory(memoryId: string | null) {
  const { getMemory } = useWorkspaceApi();
  return useQuery({
    queryKey: ["memories", "item", memoryId],
    queryFn: ({ signal }) => getMemory(memoryId!, signal),
    enabled: memoryId !== null,
  });
}
export function useMemoryActions(nodeId: number) {
  const { addMemory, deleteMemory } = useWorkspaceApi();
  const client = useQueryClient();
  const refresh = async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: ["memories", "node", nodeId] }),
      client.invalidateQueries({ queryKey: ["overview"] }),
    ]);
  };
  const add = useMutation({
    mutationFn: (payload: {
      content: string;
      tags: string[];
      intent: MemoryIntent;
    }) => addMemory(nodeId, payload),
    onSuccess: refresh,
  });
  const remove = useMutation({
    mutationFn: (memoryId: string) => deleteMemory(memoryId),
    onSuccess: async (_, memoryId) => {
      client.removeQueries({ queryKey: ["memories", "item", memoryId] });
      await refresh();
    },
  });
  return { add, remove };
}
