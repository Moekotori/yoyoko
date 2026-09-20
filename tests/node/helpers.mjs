import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

export const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "../..");
export const fixturesDir = join(repoRoot, "docs/protocol/fixtures");

export function readFixture(name) {
  return readFileSync(join(fixturesDir, name), "utf8");
}

export function baseUrl() {
  return (process.env.CHAT_BASE_URL ?? "http://127.0.0.1:18080").replace(/\/$/, "");
}

export function gatewayUrl(origin = baseUrl()) {
  return `${origin.replace(/^http/, "ws")}/gateway`;
}

export function nextMessage(socket, ms = 5000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("WebSocket message timeout")), ms);
    const onMessage = (event) => {
      clearTimeout(timer);
      socket.removeEventListener("message", onMessage);
      resolve(typeof event.data === "string" ? event.data : event.data.toString());
    };
    socket.addEventListener("message", onMessage);
  });
}

export function waitClose(socket, ms = 5000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("WebSocket close timeout")), ms);
    socket.addEventListener("close", (event) => {
      clearTimeout(timer);
      resolve(event);
    });
  });
}

export async function openGateway(url = gatewayUrl()) {
  const socket = new WebSocket(url);
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`open timeout: ${url}`)), 5000);
    socket.addEventListener("open", () => {
      clearTimeout(timer);
      resolve();
    });
    socket.addEventListener("error", () => {
      clearTimeout(timer);
      reject(new Error(`WebSocket error: ${url}`));
    });
  });
  const hello = JSON.parse(await nextMessage(socket));
  return { socket, hello };
}
