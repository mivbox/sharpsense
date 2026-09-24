import type { ChildProcess } from "node:child_process";

interface StopProcessOptions {
  gracefulTimeoutMs?: number;
  forceTimeoutMs?: number;
}

export async function stopChildProcess(
  child: ChildProcess | undefined,
  { gracefulTimeoutMs = 5000, forceTimeoutMs = 2000 }: StopProcessOptions = {},
) {
  if (!child || hasExited(child)) return;

  if (await terminateAndWait(child, "SIGTERM", gracefulTimeoutMs)) return;
  if (await terminateAndWait(child, "SIGKILL", forceTimeoutMs)) return;

  throw new Error("Child process did not exit after forced termination.");
}

function hasExited(child: ChildProcess) {
  return child.exitCode !== null || child.signalCode !== null;
}

function terminateAndWait(
  child: ChildProcess,
  signal: NodeJS.Signals,
  timeoutMs: number,
) {
  return new Promise<boolean>((resolve) => {
    const finish = (stopped: boolean) => {
      clearTimeout(timer);
      child.removeListener("exit", onExit);
      child.removeListener("error", onError);
      resolve(stopped);
    };
    const onExit = () => finish(true);
    const onError = () => finish(hasExited(child) || child.pid === undefined);
    const timer = setTimeout(() => finish(false), timeoutMs);

    // Register before signalling: even an immediate exit must settle this wait.
    child.once("exit", onExit);
    child.once("error", onError);
    if (hasExited(child)) {
      finish(true);
      return;
    }
    // A failed spawn reports its error asynchronously and has no PID to signal.
    if (child.pid === undefined) return;

    try {
      child.kill(signal);
    } catch {
      onError();
    }
  });
}
