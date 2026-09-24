import type { RequestOption } from "@microsoft/kiota-abstractions";
import type { Middleware } from "@microsoft/kiota-http-fetchlibrary";

const abortOptionKey = "SharpSense.AbortSignal";

class AbortSignalOption implements RequestOption {
  constructor(readonly signal: AbortSignal) {}

  getKey(): string {
    return abortOptionKey;
  }
}

export function requestConfiguration(signal?: AbortSignal): {
  options?: RequestOption[];
} {
  return signal ? { options: [new AbortSignalOption(signal)] } : {};
}

/** One transport attempt; TanStack Query owns retries and mutation policy. */
export class QueryFetchMiddleware implements Middleware {
  next: Middleware | undefined;

  execute(
    url: string,
    requestInit: RequestInit,
    options?: Record<string, RequestOption>,
  ): Promise<Response> {
    const cancellation = options?.[abortOptionKey];
    const signal =
      cancellation instanceof AbortSignalOption
        ? cancellation.signal
        : requestInit.signal;
    signal?.throwIfAborted();
    return fetch(url, { ...requestInit, signal });
  }
}

export class WorkspaceApiError extends Error {
  constructor(
    message: string,
    readonly responseStatusCode?: number,
    cause?: unknown,
  ) {
    super(message, { cause });
    this.name = "WorkspaceApiError";
  }
}

export async function workspaceRequest<T>(
  operation: () => Promise<T>,
  signal?: AbortSignal,
): Promise<T> {
  signal?.throwIfAborted();
  try {
    const result = await operation();
    signal?.throwIfAborted();
    return result;
  } catch (error) {
    if (signal?.aborted) throw error;
    if (error && typeof error === "object") {
      const problem = error as {
        detail?: string;
        title?: string;
        message?: string;
        responseStatusCode?: number;
      };
      throw new WorkspaceApiError(
        problem.detail ||
          problem.title ||
          problem.message ||
          "The workspace request couldn’t be completed.",
        problem.responseStatusCode,
        error,
      );
    }
    throw new Error("The workspace request couldn’t be completed.", {
      cause: error,
    });
  }
}

export function shouldRetryWorkspaceRequest(
  failureCount: number,
  error: Error,
): boolean {
  if (failureCount >= 2 || error.name === "AbortError") return false;
  if (
    error instanceof WorkspaceApiError &&
    error.responseStatusCode !== undefined
  ) {
    return error.responseStatusCode === 429 || error.responseStatusCode >= 500;
  }
  return (
    error instanceof TypeError ||
    (error instanceof WorkspaceApiError && error.cause instanceof TypeError)
  );
}
