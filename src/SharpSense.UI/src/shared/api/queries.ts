import { useQuery } from "@tanstack/react-query";
import { useWorkspaceApi } from "../workspace/context";

export function useWorkspaceOverview() {
  const api = useWorkspaceApi();
  return useQuery({
    queryKey: ["overview"],
    queryFn: ({ signal }) => api.getOverview(signal),
  });
}

export function useWorkspaceTools() {
  const api = useWorkspaceApi();
  return useQuery({
    queryKey: ["tools"],
    queryFn: ({ signal }) => api.getTools(signal),
  });
}
