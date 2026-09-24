import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CssBaseline, ThemeProvider } from "@mui/material";
import App from "./App";
import { theme } from "./app/theme";
import { shouldRetryWorkspaceRequest } from "./shared/api/transport";
import { AppErrorBoundary } from "./shared/ui/AppErrorBoundary";

const queryClient = new QueryClient({
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
const root = document.getElementById("root");
if (!root) throw new Error("Workspace root element is missing.");
createRoot(root).render(
  <StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <AppErrorBoundary>
        <QueryClientProvider client={queryClient}>
          <App />
        </QueryClientProvider>
      </AppErrorBoundary>
    </ThemeProvider>
  </StrictMode>,
);
