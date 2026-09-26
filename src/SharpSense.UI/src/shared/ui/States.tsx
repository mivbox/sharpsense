import { Alert, Box, Button, Skeleton, Stack, Typography } from "@mui/material";
import type { ReactNode } from "react";

export function EmptyState({
  icon,
  title,
  description,
  action,
  compact = false,
}: {
  icon?: ReactNode;
  title: string;
  description: string;
  action?: ReactNode;
  compact?: boolean;
}) {
  return (
    <Stack
      spacing={compact ? 1 : 1.5}
      sx={{
        alignItems: "center",
        justifyContent: "center",
        px: 3,
        py: compact ? 3 : 6,
        textAlign: "center",
        height: "100%",
        minHeight: compact ? 150 : 260,
      }}
    >
      {icon && (
        <Box
          sx={{
            display: "grid",
            placeItems: "center",
            width: compact ? 38 : 48,
            height: compact ? 38 : 48,
            mb: 0.5,
            borderRadius: 2.5,
            color: "primary.main",
          }}
        >
          {icon}
        </Box>
      )}
      <Typography variant={compact ? "subtitle2" : "h6"}>{title}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ maxWidth: 330 }}>
        {description}
      </Typography>
      {action}
    </Stack>
  );
}
export function ErrorState({
  error,
  retry,
}: {
  error: unknown;
  retry?: () => void;
}) {
  const detail =
    error instanceof Error
      ? error.message
      : "The request couldn’t be completed. Please try again.";
  return (
    <Alert
      severity="error"
      action={
        retry ? (
          <Button color="inherit" size="small" onClick={retry}>
            Retry
          </Button>
        ) : undefined
      }
    >
      {detail}
    </Alert>
  );
}
export function LoadingRows({ count = 5 }: { count?: number }) {
  return (
    <Stack
      spacing={1.5}
      sx={{ p: 2 }}
      aria-label="Loading results"
      role="status"
    >
      {Array.from({ length: count }, (_, index) => (
        <Stack
          key={index}
          direction="row"
          spacing={1.5}
          sx={{ alignItems: "center" }}
        >
          <Skeleton variant="rounded" width={28} height={28} />
          <Box sx={{ flex: 1 }}>
            <Skeleton width={`${72 - (index % 3) * 12}%`} height={16} />
            <Skeleton width="45%" height={12} />
          </Box>
        </Stack>
      ))}
    </Stack>
  );
}
