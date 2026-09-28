import { useQuery } from "@tanstack/react-query";
import { useWorkspaceApi } from "../../shared/workspace/context";

export function useGraphStats(enabled = true) {
  const api = useWorkspaceApi();
  return useQuery({
    queryKey: ["graph-stats"],
    queryFn: ({ signal }) => api.getGraphStats(signal),
    staleTime: 0,
    enabled,
  });
}
