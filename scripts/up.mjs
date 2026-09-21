#!/usr/bin/env node
import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import os from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const win = process.platform === "win32";
const args = new Set(process.argv.slice(2));
const lan = args.has("--lan");
const build = !args.has("--no-build");
const down = args.has("--down");

function dockerBin() {
  if (process.env.DOCKER) return process.env.DOCKER;
  if (win) {
    const desktop = "C:\\Program Files\\Docker\\Docker\\resources\\bin\\docker.exe";
    if (existsSync(desktop)) return desktop;
  }
  return "docker";
}

function run(command, argv, options = {}) {
  const result = spawnSync(command, argv, {
    cwd: root,
    stdio: "inherit",
    shell: false,
    env: options.env ?? process.env,
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
}

function dockerInfo() {
  const result = spawnSync(dockerBin(), ["info"], {
    cwd: root,
    stdio: "ignore",
    shell: false,
  });
  return result.status === 0;
}

function startDockerDesktop() {
  if (!win) return;
  const app = "C:\\Program Files\\Docker\\Docker\\Docker Desktop.exe";
  if (!existsSync(app)) return;
  console.log("正在启动 Docker Desktop…");
  spawnSync(app, [], { cwd: root, stdio: "ignore", shell: false, detached: true });
}

function lanAddresses() {
  const found = [];
  for (const [name, addrs] of Object.entries(os.networkInterfaces())) {
    for (const addr of addrs ?? []) {
      const v4 = addr.family === "IPv4" || addr.family === 4;
      if (addr.internal || !v4) continue;
      const parts = addr.address.split(".").map(Number);
      const privateRange =
        parts[0] === 10 ||
        (parts[0] === 172 && parts[1] >= 16 && parts[1] <= 31) ||
        (parts[0] === 192 && parts[1] === 168);
      if (!privateRange) continue;
      found.push({ name, address: addr.address });
    }
  }
  return found;
}

async function waitReady(url, seconds) {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${url}/health/ready`, {
        signal: AbortSignal.timeout(2000),
      });
      if (response.ok) return;
    } catch {
      // Retry until Postgres and the API accept connections.
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error(`等待 ${url}/health/ready 超时`);
}

if (!dockerInfo()) {
  startDockerDesktop();
  const deadline = Date.now() + 120_000;
  while (!dockerInfo() && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 3000));
  }
  if (!dockerInfo()) {
    console.error("Docker 引擎未运行。请先打开 Docker Desktop，再执行 docker compose up -d --build");
    process.exit(1);
  }
}

const env = { ...process.env };
if (lan) env.CHAT_PUBLISH = "0.0.0.0";
else env.CHAT_PUBLISH ??= "127.0.0.1";

if (down) {
  run(dockerBin(), ["compose", "down"], { env });
  process.exit(0);
}

const compose = ["compose", "up", "-d"];
if (build) compose.push("--build");
console.log(lan ? "正在启动局域网 Docker 服务端…" : "正在启动本机 Docker 服务端…");
run(dockerBin(), compose, { env });

const local = "http://127.0.0.1:8080";
await waitReady(local, 90);

const interfaces = lanAddresses();
console.log("");
console.log("服务已就绪（开发凭据，明文 HTTP，不是生产安装）");
console.log(`本机     ${local}`);
console.log("发现     http://localhost:8080/.well-known/lightchat");
if (lan || env.CHAT_PUBLISH === "0.0.0.0") {
  for (const item of interfaces) {
    console.log(`局域网   http://${item.address}:8080  (${item.name})`);
  }
}
console.log("停止     docker compose down");
console.log("");
