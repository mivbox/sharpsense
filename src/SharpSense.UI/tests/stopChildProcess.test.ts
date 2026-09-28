import assert from "node:assert/strict";
import { ChildProcess, spawn } from "node:child_process";
import { once } from "node:events";
import test from "node:test";
import { stopChildProcess } from "../e2e/stopChildProcess";

const timeouts = { gracefulTimeoutMs: 100, forceTimeoutMs: 1000 };

async function runningChild(onSignal = "") {
  const child = spawn(
    process.execPath,
    ["-e", `${onSignal}; setInterval(() => {}, 1000); process.send("ready");`],
    { stdio: ["ignore", "ignore", "ignore", "ipc"] },
  );
  await once(child, "message");
  return child;
}

test("cleanup accepts an absent child", async () => {
  await stopChildProcess(undefined, timeouts);
});

test(
  "cleanup returns after a previous normal exit",
  { timeout: 5000 },
  async () => {
    const child = spawn(process.execPath, ["-e", "process.exit(0)"], {
      stdio: "ignore",
    });
    await once(child, "exit");

    await stopChildProcess(child, timeouts);

    assert.equal(child.exitCode, 0);
  },
);

test(
  "cleanup returns after a previous signal exit",
  { timeout: 5000 },
  async () => {
    const child = await runningChild();
    const exited = once(child, "exit");
    child.kill("SIGTERM");
    await exited;
    assert.equal(child.exitCode, null);
    assert.equal(child.signalCode, "SIGTERM");

    await stopChildProcess(child, timeouts);
  },
);

test(
  "cleanup waits for graceful shutdown",
  { timeout: 5000, skip: process.platform === "win32" },
  async (context) => {
    const child = await runningChild(
      'process.on("SIGTERM", () => process.exit(0))',
    );
    context.after(() => child.kill("SIGKILL"));

    await stopChildProcess(child, { ...timeouts, gracefulTimeoutMs: 1000 });

    assert.equal(child.exitCode, 0);
    assert.equal(child.signalCode, null);
  },
);

test(
  "cleanup kills a child that ignores graceful termination",
  { timeout: 5000, skip: process.platform === "win32" },
  async (context) => {
    const child = await runningChild('process.on("SIGTERM", () => {})');
    context.after(() => child.kill("SIGKILL"));

    await stopChildProcess(child, timeouts);

    assert.equal(child.exitCode, null);
    assert.equal(child.signalCode, "SIGKILL");
  },
);

test("cleanup handles a failed spawn", { timeout: 5000 }, async () => {
  const child = spawn(process.execPath + ".missing", [], { stdio: "ignore" });
  const errors: Error[] = [];
  child.on("error", (error) => errors.push(error));

  await stopChildProcess(child, timeouts);

  assert.equal(child.pid, undefined);
  assert.equal(errors.length, 1);
  assert.equal((errors[0] as NodeJS.ErrnoException).code, "ENOENT");
});

test("cleanup bounds an unsuccessful forced termination", async (context) => {
  const child = new ChildProcess();
  Object.defineProperty(child, "pid", { value: 123 });
  const kill = context.mock.method(child, "kill", () => true);

  await assert.rejects(
    stopChildProcess(child, { gracefulTimeoutMs: 10, forceTimeoutMs: 10 }),
    /did not exit after forced termination/,
  );

  assert.deepEqual(
    kill.mock.calls.map((call) => call.arguments[0]),
    ["SIGTERM", "SIGKILL"],
  );
  assert.equal(child.listenerCount("exit"), 0);
  assert.equal(child.listenerCount("error"), 0);
});
