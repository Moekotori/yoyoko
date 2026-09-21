#!/usr/bin/env node
import { spawn, spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import os from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const win = process.platform === "win32";
const port = Number(process.env.CHAT_LAN_PORT ?? 8080);
if (!Number.isInteger(port) || port < 1 || port > 65535) {
  throw new Error("CHAT_LAN_PORT must be 1..65535");
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
        (parts[0] === 192 && parts[1] === 168) ||
        (parts[0] === 169 && parts[1] === 254);
      if (!privateRange) continue;
      found.push({
        name,
        address: addr.address,
        linkLocal: parts[0] === 169,
      });
    }
  }
  found.sort((a, b) => Number(a.linkLocal) - Number(b.linkLocal));
  return found;
}

function serverBinary() {
  if (process.env.CHAT_SERVER_BIN) return process.env.CHAT_SERVER_BIN;
  return join(root, "target/debug", win ? "chat-server.exe" : "chat-server");
}

function run(command, args, env = process.env) {
  const result = spawnSync(command, args, {
    cwd: root,
    stdio: "inherit",
    shell: win,
    env,
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
}

async function waitHealthy(url, child) {
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(`chat-server exited ${child.exitCode} before becoming healthy`);
    }
    try {
      const response = await fetch(`${url}/health/live`, { signal: AbortSignal.timeout(1000) });
      if (response.ok) return;
    } catch {
      // Retry until listen succeeds.
    }
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
  throw new Error(`timed out waiting for ${url}/health/live`);
}

const interfaces = lanAddresses();
const primary = process.env.CHAT_LAN_HOST ?? interfaces.find((item) => !item.linkLocal)?.address ?? interfaces[0]?.address;
if (!primary) {
  console.error("没有可用的局域网 IPv4。连接 Wi-Fi/以太网后再试，或设置 CHAT_LAN_HOST。");
  process.exit(1);
}

const origin = `http://${primary}:${port}`;
const local = `http://127.0.0.1:${port}`;
const origins = [
  origin,
  local,
  `http://localhost:${port}`,
  ...interfaces.map((item) => `http://${item.address}:${port}`),
].filter((value, index, all) => all.indexOf(value) === index);

if (!process.env.CHAT_SERVER_BIN) {
  const up = spawnSync(process.execPath, [join(root, "scripts/up.mjs"), "--lan", "--no-build"], {
    cwd: root,
    stdio: "inherit",
    env: process.env,
  });
  if (up.status === 0) {
    console.log(`客户端输入  ${origin}`);
    console.log(`网页语音  ${origin}/voice/<channel-id>`);
    process.exit(0);
  }
  console.log("Docker 不可用，回退到本机 cargo 服务端。");
}

const bin = serverBinary();
if (!process.env.CHAT_SERVER_BIN) {
  run("cargo", ["build", "-p", "chat-server"]);
}
if (!existsSync(bin)) {
  console.error(`chat-server binary not found: ${bin}`);
  process.exit(1);
}

const env = {
  ...process.env,
  CHAT__SERVER__LISTEN: `0.0.0.0:${port}`,
  CHAT__SERVER__PUBLIC_URL: origin,
  CHAT__SERVER__ALLOWED_ORIGINS: origins.join(","),
  CHAT__STORAGE__PUBLIC_URL: `${origin}/api/v1`,
  CHAT__RTC__PUBLIC_URL: `http://${primary}:7880`,
};

const child = spawn(bin, [], { cwd: root, env, stdio: "inherit" });
const stop = () => {
  if (child.exitCode === null) child.kill("SIGTERM");
};
process.on("exit", stop);
process.on("SIGINT", () => {
  stop();
  process.exit(130);
});
process.on("SIGTERM", () => {
  stop();
  process.exit(143);
});

await waitHealthy(local, child);

console.log("");
console.log("局域网模式已启动（仅用于本机/内网测试，明文 HTTP）");
console.log(`监听     0.0.0.0:${port}`);
console.log(`本机     ${local}`);
for (const item of interfaces) {
  console.log(`局域网   http://${item.address}:${port}  (${item.name})`);
}
console.log(`客户端输入  ${origin}`);
console.log(`网页语音  ${origin}/voice/<channel-id>`);
console.log("Ctrl+C 停止");
console.log("");

await new Promise((resolve) => child.on("exit", resolve));
