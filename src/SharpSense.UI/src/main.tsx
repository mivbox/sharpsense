import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import { CssBaseline, ThemeProvider } from "@mui/material";
import App from "./App";
import { theme } from "./app/theme";
import { createQueryClient } from "./shared/api/queryClient";
import { AppErrorBoundary } from "./shared/ui/AppErrorBoundary";

const queryClient = createQueryClient();
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
