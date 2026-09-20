import assert from "node:assert/strict";
import test from "node:test";
import { baseUrl, gatewayUrl, openGateway, waitClose } from "./helpers.mjs";

const origin = baseUrl();

async function getJson(path) {
  const response = await fetch(`${origin}${path}`, { signal: AbortSignal.timeout(5000) });
  const body = await response.json();
  return { response, body };
}

test("health live returns process liveness", async () => {
  const { response, body } = await getJson("/health/live");
  assert.equal(response.status, 200);
  assert.equal(body.status, "ok");
});

test("well-known discovery is versioned", async () => {
  const { response, body } = await getJson("/.well-known/lightchat");
  assert.equal(response.status, 200);
  assert.equal(body.protocol_version, 1);
  assert.equal(body.api_version, 1);
  assert.match(body.api, /\/api\/v1$/);
  assert.match(body.gateway, /^wss?:\/\//);
  assert.match(body.gateway, /\/gateway$/);
});

test("business API is closed", async () => {
  const { response, body } = await getJson("/api/v1/messages");
  assert.equal(response.status, 404);
  assert.equal(body.code, "not_found");
});

test("gateway hello then rejects incompatible protocol", async () => {
  const { socket, hello } = await openGateway(gatewayUrl(origin));
  assert.equal(hello.op, "hello");
  assert.equal(hello.data.protocol_version, 1);
  assert.equal(typeof hello.data.heartbeat_interval_ms, "number");
  socket.send(
    JSON.stringify({
      op: "identify",
      event: null,
      seq: null,
      data: { protocol_version: 999, access_token: "not-a-token" },
    }),
  );
  const closed = await waitClose(socket);
  assert.equal(closed.code, 4406);
});

test("gateway identify without auth is not implemented", async () => {
  const { socket } = await openGateway(gatewayUrl(origin));
  socket.send(
    JSON.stringify({
      op: "identify",
      event: null,
      seq: null,
      data: { protocol_version: 1, access_token: "not-a-token" },
    }),
  );
  const closed = await waitClose(socket);
  assert.equal(closed.code, 4401);
});
