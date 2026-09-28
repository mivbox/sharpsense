import { Component, type ReactNode } from "react";
import { Alert, Box, Button, Stack, Typography } from "@mui/material";

export class AppErrorBoundary extends Component<
  { children: ReactNode },
  { error: Error | null }
> {
  state: { error: Error | null } = { error: null };
  static getDerivedStateFromError(error: Error) {
    return { error };
  }
  render() {
    if (!this.state.error) return this.props.children;
    return (
      <Box
        sx={{ minHeight: "100vh", display: "grid", placeItems: "center", p: 3 }}
      >
        <Stack spacing={2} sx={{ maxWidth: 480 }}>
          <Typography variant="h5">
            The workspace couldn’t be displayed.
          </Typography>
          <Typography color="text.secondary">
            Your index is safe. Reload the workspace to try again.
          </Typography>
          <Alert severity="error">{this.state.error.message}</Alert>
          <Button variant="contained" onClick={() => window.location.reload()}>
            Reload workspace
          </Button>
        </Stack>
      </Box>
    );
  }
}
