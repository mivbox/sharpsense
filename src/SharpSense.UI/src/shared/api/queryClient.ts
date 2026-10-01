import { QueryClient } from "@tanstack/react-query";
import { shouldRetryWorkspaceRequest } from "./transport";

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: shouldRetryWorkspaceRequest,
        retryDelay: 1000,
        refetchOnWindowFocus: false,
      },
      mutations: { retry: false },
    },
  });
}
