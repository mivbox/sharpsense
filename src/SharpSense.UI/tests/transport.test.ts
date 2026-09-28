import assert from "node:assert/strict";
import test from "node:test";
import { AnonymousAuthenticationProvider } from "@microsoft/kiota-abstractions";
import { DefaultRequestAdapter } from "@microsoft/kiota-bundle";
import { HttpClient } from "@microsoft/kiota-http-fetchlibrary";
import { createSharpSenseClient } from "../src/shared/api/generated/sharpSenseClient";
import {
  QueryFetchMiddleware,
  requestConfiguration,
  shouldRetryWorkspaceRequest,
  WorkspaceApiError,
  workspaceRequest,
} from "../src/shared/api/transport";

function client() {
  const adapter = new DefaultRequestAdapter(
    new AnonymousAuthenticationProvider(),
    undefined,
    undefined,
    new HttpClient(undefined, new QueryFetchMiddleware()),
  );
  adapter.baseUrl = "http://localhost:57000";
  return createSharpSenseClient(adapter);
}

test("query cancellation reaches fetch through the generated Kiota client", async (context) => {
  const controller = new AbortController();
  let notifyStarted!: () => void;
  const started = new Promise<void>((resolve) => {
    notifyStarted = resolve;
  });
  context.mock.method(
    globalThis,
    "fetch",
    (_url: string, init: RequestInit) => {
      assert.equal(init.signal, controller.signal);
      notifyStarted();
      return new Promise<Response>((_resolve, reject) => {
        init.signal!.addEventListener(
          "abort",
          () => reject(init.signal!.reason),
          { once: true },
        );
      });
    },
  );

  const pending = client().api.overview.get(
    requestConfiguration(controller.signal),
  );
  await started;
  controller.abort();
  await assert.rejects(pending, { name: "AbortError" });
});

test("cancelling one query does not cancel another concurrent request", async (context) => {
  const first = new AbortController();
  const second = new AbortController();
  let notifyStarted!: () => void;
  const started = new Promise<void>((resolve) => {
    notifyStarted = resolve;
  });
  context.mock.method(globalThis, "fetch", (url: string, init: RequestInit) => {
    if (url.endsWith("/overview")) {
      assert.equal(init.signal, first.signal);
      notifyStarted();
      return new Promise<Response>((_resolve, reject) => {
        init.signal!.addEventListener(
          "abort",
          () => reject(init.signal!.reason),
          { once: true },
        );
      });
    }
    assert.equal(init.signal, second.signal);
    return Promise.resolve(
      new Response("[]", { headers: { "Content-Type": "application/json" } }),
    );
  });

  const api = client();
  const pending = api.api.overview.get(requestConfiguration(first.signal));
  await started;
  const independent = api.api.tools.get(requestConfiguration(second.signal));
  first.abort();
  await assert.rejects(pending, { name: "AbortError" });
  assert.deepEqual(await independent, []);
  assert.equal(second.signal.aborted, false);
});

test("the transport does not retry a failed memory mutation", async (context) => {
  const fetch = context.mock.method(
    globalThis,
    "fetch",
    async () =>
      new Response(
        JSON.stringify({
          title: "Unavailable",
          detail: "Please try again later.",
          status: 503,
        }),
        {
          status: 503,
          headers: { "Content-Type": "application/problem+json" },
        },
      ),
  );
  await assert.rejects(
    client().api.memory.node.byNodeId(42).post({
      content: "An important invariant",
      intent: "Invariant",
      tags: [],
    }),
  );
  assert.equal(fetch.mock.callCount(), 1);
});

test("query retries are bounded and exclude validation, missing nodes, and cancellations", () => {
  assert.equal(
    shouldRetryWorkspaceRequest(0, new WorkspaceApiError("Invalid", 400)),
    false,
  );
  assert.equal(
    shouldRetryWorkspaceRequest(0, new WorkspaceApiError("Missing", 404)),
    false,
  );
  assert.equal(
    shouldRetryWorkspaceRequest(0, new DOMException("Cancelled", "AbortError")),
    false,
  );
  assert.equal(
    shouldRetryWorkspaceRequest(0, new WorkspaceApiError("Busy", 429)),
    true,
  );
  assert.equal(
    shouldRetryWorkspaceRequest(1, new WorkspaceApiError("Unavailable", 503)),
    true,
  );
  assert.equal(
    shouldRetryWorkspaceRequest(2, new WorkspaceApiError("Unavailable", 503)),
    false,
  );
  assert.equal(
    shouldRetryWorkspaceRequest(
      0,
      new WorkspaceApiError(
        "Offline",
        undefined,
        new TypeError("Failed to fetch"),
      ),
    ),
    true,
  );
  assert.equal(
    shouldRetryWorkspaceRequest(0, new Error("Invalid response shape")),
    false,
  );
});

for (const status of [400, 404, 409]) {
  test(`ProblemDetails ${status} preserves HTTP status and detail through the workspace adapter`, async (context) => {
    const detail =
      status === 400
        ? "nodeId must be positive."
        : status === 404
          ? "No indexed code node exists for id 42."
          : "Graph changed while loading. Restart from the first page.";
    context.mock.method(
      globalThis,
      "fetch",
      async () =>
        new Response(
          JSON.stringify({ title: "Request failed", detail, status }),
          { status, headers: { "Content-Type": "application/problem+json" } },
        ),
    );
    await assert.rejects(
      workspaceRequest(async () =>
        status === 409
          ? await client().api.graph.nodes.page.get()
          : await client().api.tools.context.post({ nodeId: 42 }),
      ),
      (error: unknown) => {
        assert.ok(error instanceof WorkspaceApiError);
        assert.equal(error.responseStatusCode, status);
        assert.equal(error.message, detail);
        assert.equal(shouldRetryWorkspaceRequest(0, error), false);
        return true;
      },
    );
  });
}

test("unexpected HTTP errors retain their status for bounded read retries", async (context) => {
  context.mock.method(
    globalThis,
    "fetch",
    async () =>
      new Response(JSON.stringify({ title: "Unavailable", status: 503 }), {
        status: 503,
        headers: { "Content-Type": "application/problem+json" },
      }),
  );
  await assert.rejects(
    workspaceRequest(() => client().api.overview.get()),
    (error: unknown) => {
      assert.ok(error instanceof WorkspaceApiError);
      assert.equal(error.responseStatusCode, 503);
      assert.equal(shouldRetryWorkspaceRequest(0, error), true);
      assert.equal(shouldRetryWorkspaceRequest(2, error), false);
      return true;
    },
  );
});

for (const kind of ["nodes", "edges"] as const) {
  test(`generated graph ${kind} pages preserve workspace, cursor, revision, and cancellation`, async (context) => {
    const controller = new AbortController();
    const workspaceId = "851e3463-80bf-49ba-8458-25a87db698f8";
    let notifyStarted!: () => void;
    const started = new Promise<void>((resolve) => {
      notifyStarted = resolve;
    });
    context.mock.method(
      globalThis,
      "fetch",
      (url: string, init: RequestInit) => {
        const requested = new URL(url);
        assert.equal(requested.pathname, `/api/graph/${kind}/page`);
        assert.equal(requested.searchParams.get("cursor"), "cursor/next+page=");
        assert.equal(requested.searchParams.get("revision"), "revision-42");
        assert.equal(requested.searchParams.get("pageSize"), "5000");
        assert.equal(
          new Headers(init.headers).get("X-SharpSense-Workspace"),
          workspaceId,
        );
        assert.equal(init.signal, controller.signal);
        notifyStarted();
        return new Promise<Response>((_resolve, reject) => {
          init.signal!.addEventListener(
            "abort",
            () => reject(init.signal!.reason),
            { once: true },
          );
        });
      },
    );
    const api = client();
    const pending = api.api.graph[kind].page.get({
      ...requestConfiguration(controller.signal),
      headers: { "X-SharpSense-Workspace": workspaceId },
      queryParameters: {
        directoryIds: [7, 11],
        cursor: "cursor/next+page=",
        revision: "revision-42",
        pageSize: 5000,
        includeTotal: false,
      },
    });
    await started;
    controller.abort();
    await assert.rejects(pending, { name: "AbortError" });
  });
}

test("already-cancelled workspace queries never reach fetch", async (context) => {
  const controller = new AbortController();
  controller.abort();
  const fetch = context.mock.method(
    globalThis,
    "fetch",
    async () => new Response("{}"),
  );
  await assert.rejects(
    workspaceRequest(
      () => client().api.overview.get(requestConfiguration(controller.signal)),
      controller.signal,
    ),
    { name: "AbortError" },
  );
  assert.equal(fetch.mock.callCount(), 0);
});

test("generated inspector pages bind node, workspace, revision, cursor and cancellation", async (context) => {
  const controller = new AbortController();
  const workspaceId = "b608be30-c0d1-4d5a-bc86-923f7bb19854";
  let notifyStarted!: () => void;
  const started = new Promise<void>((resolve) => {
    notifyStarted = resolve;
  });
  context.mock.method(globalThis, "fetch", (url: string, init: RequestInit) => {
    const requested = new URL(url);
    assert.equal(requested.pathname, "/api/graph/nodes/42/connections");
    assert.equal(requested.searchParams.get("cursor"), "next/peer+page=");
    assert.equal(requested.searchParams.get("revision"), "revision-17");
    assert.equal(requested.searchParams.get("pageSize"), "20");
    assert.equal(requested.searchParams.has("directoryIds"), false);
    assert.equal(
      new Headers(init.headers).get("X-SharpSense-Workspace"),
      workspaceId,
    );
    assert.equal(init.signal, controller.signal);
    notifyStarted();
    return new Promise<Response>((_resolve, reject) => {
      init.signal!.addEventListener(
        "abort",
        () => reject(init.signal!.reason),
        { once: true },
      );
    });
  });
  const pending = client()
    .api.graph.nodes.byNodeId(42)
    .connections.get({
      ...requestConfiguration(controller.signal),
      headers: { "X-SharpSense-Workspace": workspaceId },
      queryParameters: {
        pageSize: 20,
        cursor: "next/peer+page=",
        revision: "revision-17",
        includeTotal: false,
      },
    });
  await started;
  controller.abort();
  await assert.rejects(pending, { name: "AbortError" });
});

for (const status of [404, 409]) {
  test(`generated inspector ${status} retains typed problem details without retries`, async (context) => {
    const detail =
      status === 404
        ? "No graph node exists for id 42."
        : "The workspace graph changed while loading.";
    const fetch = context.mock.method(
      globalThis,
      "fetch",
      async () =>
        new Response(
          JSON.stringify({ status, title: "Request failed", detail }),
          { status, headers: { "Content-Type": "application/problem+json" } },
        ),
    );
    await assert.rejects(
      workspaceRequest(() =>
        client().api.graph.nodes.byNodeId(42).connections.get(),
      ),
      (error: unknown) => {
        assert.ok(error instanceof WorkspaceApiError);
        assert.equal(error.responseStatusCode, status);
        assert.equal(error.message, detail);
        assert.equal(shouldRetryWorkspaceRequest(0, error), false);
        return true;
      },
    );
    assert.equal(fetch.mock.callCount(), 1);
  });
}
