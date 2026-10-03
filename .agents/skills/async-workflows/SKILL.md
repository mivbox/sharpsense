---
name: async-workflows
description: "Review or change concurrent workers, background jobs and streams: operation ownership, cancellation, buffering and shutdown."
---

# Async workflows

Identify what owns the operation, its dependencies and its completion state. Read the existing lifecycle and
concurrency tests before changing scheduling, cancellation or streaming behavior.

## Own the lifetime

- Keep request/tenant/workspace identity attached to the operation that owns it. Resolve scoped dependencies
  within that lifetime and dispose them after all dependent work finishes.
- Bound concurrency. Preserve required ordering within a partition and make merging deterministic where results
  depend on order. Do not share a non-thread-safe dependency merely because calls are asynchronous.
- Observe background tasks through their owner, such as a hosted service or explicit job. Preserve failures and
  cancellation through shutdown; do not discard a task or suppress its warning.
- Propagate cancellation and release subscriptions, leases, processes and buffers on every exit path. Distinguish
  cancellation from timeout and successful completion; transport-specific exit policy belongs at the boundary.

## Keep progress and data bounded

- Reject obsolete updates when work has been superseded. A late callback must not regress completed state.
- Define bounded buffering and how a slow consumer catches up. Avoid blocking the producer on presentation unless
  that is the explicit contract.
- Use streaming where it reduces retained data without breaking ordering, disposal or consistency. Retain
  materialisation where the operation needs a complete snapshot. Add pooling only for demonstrated allocation cost.
- Retry only operations with a safe retry contract, with bounded attempts/backoff and preserved cancellation.
  Reuse the repository's resilience mechanism rather than adding a parallel stack.

Validate relevant lifecycle paths: cancellation during work, failure, shutdown, competing jobs, stale progress,
slow consumers or reconnects. Use signals and bounded conditions to exercise races deterministically. Confirm that
resources are released and the final outcome remains accurate.
