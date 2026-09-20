#!/usr/bin/env node
import { spawn, spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const win = process.platform === "win32";
const args = process.argv.slice(2);
const flags = new Set(args.filter((value) => value.startsWith("-")));
const requested = args.filter((value) => !value.startsWith("-"));
const attach = flags.has("--attach");
const full = flags.has("--full");
const jobs = requested.length > 0 ? requested : full
  ? ["protocol", "live", "desktop", "native", "structure", "server"]
  : ["protocol", "live"];

function which(command) {
  const probe = win ? "where" : "which";
  return spawnSync(probe, [command], { stdio: "ignore" }).status === 0;
}

function run(command, commandArgs, env = process.env) {
  console.log(`$ ${command} ${commandArgs.join(" ")}`);
  const result = spawnSync(command, commandArgs, {
    cwd: root,
    env,
    stdio: "inherit",
    shell: win,
  });
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

function serverBinary() {
  if (process.env.CHAT_SERVER_BIN) {
    return process.env.CHAT_SERVER_BIN;
  }
  const name = win ? "chat-server.exe" : "chat-server";
  return join(root, "target/debug", name);
}

async function waitHealthy(url, child, timeoutMs = 30000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(`chat-server exited ${child.exitCode} before becoming healthy`);
    }
    try {
      const response = await fetch(`${url}/health/live`, { signal: AbortSignal.timeout(1000) });
      if (response.ok) {
        return;
      }
    } catch {
      // Retry until the process listens or the timeout elapses.
    }
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
  throw new Error(`timed out waiting for ${url}/health/live`);
}

function stop(child) {
  if (!child || child.exitCode !== null) {
    return;
  }
  child.kill("SIGTERM");
  setTimeout(() => {
    if (child.exitCode === null) {
      child.kill("SIGKILL");
    }
  }, 2000).unref();
}

async function runLive() {
  const origin = (process.env.CHAT_BASE_URL ?? "http://127.0.0.1:18080").replace(/\/$/, "");
  const parsed = new URL(origin);
  const listen = parsed.port ? parsed.host : `${parsed.hostname}:${parsed.protocol === "https:" ? "443" : "80"}`;
  let child;
  if (attach && !process.env.CHAT_BASE_URL) {
    throw new Error("--attach requires CHAT_BASE_URL");
  }
  const startLocal = !attach && !process.env.CHAT_BASE_URL;
  if (startLocal) {
    const bin = serverBinary();
    if (!process.env.CHAT_SERVER_BIN && !existsSync(bin)) {
      run("cargo", ["build", "--locked", "-p", "chat-server"]);
    }
    if (!existsSync(bin)) {
      throw new Error(`chat-server binary not found: ${bin}`);
    }
    const env = {
      ...process.env,
      CHAT_CONFIG: process.env.CHAT_CONFIG ?? join(root, "tests/node/config.toml"),
      CHAT__SERVER__LISTEN: listen,
      CHAT__SERVER__PUBLIC_URL: origin,
      CHAT__SERVER__ALLOWED_ORIGINS: origin,
    };
    console.log(`$ ${bin}`);
    child = spawn(bin, [], { cwd: root, env, stdio: "inherit" });
    process.on("exit", () => stop(child));
    process.on("SIGINT", () => {
      stop(child);
      process.exit(130);
    });
    await waitHealthy(origin, child);
  }
  try {
    run("node", ["--test", "tests/node/live.test.mjs"], {
      ...process.env,
      CHAT_BASE_URL: origin,
    });
  } finally {
    stop(child);
  }
}

const handlers = {
  protocol() {
    run("node", ["--test", "tests/node/fixtures.test.mjs"]);
  },
  async live() {
    await runLive();
  },
  desktop() {
    run("dotnet", ["restore", "Chat.sln", "--locked-mode"]);
    run("dotnet", ["build", "Chat.sln", "-c", "Release", "--no-restore"]);
    run("dotnet", ["run", "--project", "tests/client", "-c", "Release", "--no-build"]);
  },
  native() {
    run("cmake", ["-S", "native-media-core", "-B", "build/native", "-DCMAKE_BUILD_TYPE=Release"]);
    run("cmake", ["--build", "build/native", "--config", "Release"]);
    run("ctest", ["--test-dir", "build/native", "-C", "Release", "--output-on-failure"]);
  },
  structure() {
    run("dotnet", ["restore", "Chat.sln", "--locked-mode"]);
    run("dotnet", ["format", "whitespace", "Chat.sln", "--verify-no-changes", "--no-restore"]);
    run(which("python3") ? "python3" : "python", ["scripts/check_boundaries.py"]);
    if (which("docker") || process.env.GITHUB_ACTIONS) {
      run("docker", ["compose", "config", "--quiet"]);
    } else {
      console.log("skip docker compose config (docker not installed)");
    }
  },
  server() {
    run("cargo", ["fmt", "--all", "--check"]);
    run("cargo", ["clippy", "--workspace", "--all-targets", "--locked", "--", "-D", "warnings"]);
    run("cargo", ["test", "--workspace", "--locked"]);
    run("cargo", ["run", "--locked", "-p", "chat-server", "--", "migrate"]);
    run("cargo", ["run", "--locked", "-p", "chat-server", "--", "migrate"]);
    run("cargo", ["build", "--release", "--locked", "-p", "chat-server"]);
  },
};

for (const job of jobs) {
  if (!handlers[job]) {
    console.error(`Unknown job: ${job}`);
    console.error(`Jobs: ${Object.keys(handlers).join(", ")}`);
    process.exit(2);
  }
  console.log(`\n== ${job} ==`);
  await handlers[job]();
}
