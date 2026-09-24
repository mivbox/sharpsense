import { AnonymousAuthenticationProvider } from "@microsoft/kiota-abstractions";
import { DefaultRequestAdapter } from "@microsoft/kiota-bundle";
import { HttpClient } from "@microsoft/kiota-http-fetchlibrary";
import { createSharpSenseClient } from "./generated/sharpSenseClient";
import { QueryFetchMiddleware } from "./transport";

/** Kiota owns request construction; TanStack Query owns retries and cancellation. */
export function createApiClient() {
  const adapter = new DefaultRequestAdapter(
    new AnonymousAuthenticationProvider(),
    undefined,
    undefined,
    new HttpClient(undefined, new QueryFetchMiddleware()),
  );
  adapter.baseUrl = (
    import.meta.env.VITE_API_BASE_URL ?? window.location.origin
  ).replace(/\/$/, "");
  return createSharpSenseClient(adapter);
}

export const apiClient = createApiClient();
