import { queryOptions } from "@tanstack/react-query";
import { listWorkspaces } from "./api";

export const workspaceCatalogOptions = queryOptions({
  queryKey: ["workspace-catalog"],
  queryFn: ({ signal }) => listWorkspaces(signal),
  refetchInterval: 10_000,
});
