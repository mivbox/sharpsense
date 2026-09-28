import { JsonParseNodeFactory } from "@microsoft/kiota-serialization-json";
import {
  createWorkspaceIndexingStatusFromDiscriminatorValue,
  type WorkspaceIndexingStatus,
} from "../../shared/api/generated/models";

const parseNodeFactory = new JsonParseNodeFactory();

export function parseIndexingEvent(
  payload: string,
  workspaceId: string,
): WorkspaceIndexingStatus {
  const root = parseNodeFactory.getRootParseNode(
    "application/json",
    new TextEncoder().encode(payload).buffer,
  );
  const status = root.getObjectValue<WorkspaceIndexingStatus>(
    createWorkspaceIndexingStatusFromDiscriminatorValue,
  );
  if (
    status?.workspaceId !== workspaceId ||
    !status.streamId ||
    !Number.isSafeInteger(status.sequence) ||
    (status.sequence ?? -1) < 0 ||
    !status.state ||
    !(status.updatedAt instanceof Date) ||
    !Number.isFinite(status.updatedAt.getTime())
  )
    throw new Error(
      "The analysis stream returned an invalid workspace status.",
    );
  return status;
}

/** Native retries handle open streams; failed HTTP handshakes need a new source. */
export function subscribeToIndexing(
  url: string,
  workspaceId: string,
  onStatus: (status: WorkspaceIndexingStatus) => void,
  onConnection: (connected: boolean) => void,
  createSource: (url: string) => EventSource = (value) =>
    new EventSource(value),
): () => void {
  let source: EventSource | undefined;
  let reconnect: ReturnType<typeof setTimeout> | undefined;
  let disposed = false;
  const receive = (event: MessageEvent<string>) => {
    if (disposed) return;
    try {
      const status = parseIndexingEvent(event.data, workspaceId);
      onStatus(status);
      onConnection(true);
    } catch {
      // Keep polling available if a proxy or incompatible server sends bad data.
      onConnection(false);
    }
  };
  function connect() {
    if (disposed) return;
    try {
      source = createSource(url);
      source.addEventListener("status", receive);
      source.addEventListener("error", disconnected);
    } catch {
      onConnection(false);
      retry();
    }
  }

  function retry() {
    reconnect = setTimeout(connect, 3000);
  }

  function detach() {
    source?.removeEventListener("status", receive);
    source?.removeEventListener("error", disconnected);
    source?.close();
    source = undefined;
  }

  function disconnected() {
    if (disposed) return;
    onConnection(false);
    if (source?.readyState === 2) {
      detach();
      retry();
    }
  }

  connect();

  return () => {
    disposed = true;
    clearTimeout(reconnect);
    detach();
  };
}
