import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CssBaseline, ThemeProvider, createTheme } from "@mui/material";
import App from "./App";

const queryClient = new QueryClient();
const theme = createTheme({
  palette: {
    mode: "dark",
    background: {
      default: "#05070d",
      paper: "#111827"
    },
    primary: {
      main: "#60a5fa"
    },
    secondary: {
      main: "#22c55e"
    }
  },
  shape: {
    borderRadius: 16
  }
});

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error("The UI root element was not found.");
}

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <App />
      </ThemeProvider>
    </QueryClientProvider>
  </StrictMode>
);
