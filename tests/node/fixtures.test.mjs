import assert from "node:assert/strict";
import test from "node:test";
import { readFixture } from "./helpers.mjs";

const SEQ = "9007199254740993";

test("discovery fixture has versioned origins", () => {
  const discovery = JSON.parse(readFixture("discovery.json"));
  assert.equal(discovery.protocol_version, 1);
  assert.equal(discovery.api_version, 1);
  assert.equal(discovery.api, "http://localhost:8080/api/v1");
  assert.equal(discovery.gateway, "ws://localhost:8080/gateway");
  assert.match(discovery.instance_id, /^[0-9a-f-]{36}$/);
});

test("message seq stays a decimal string past JS MAX_SAFE_INTEGER", () => {
  const raw = readFixture("message-create.json");
  assert.match(raw, /"seq"\s*:\s*"9007199254740993"/);
  const envelope = JSON.parse(raw);
  assert.equal(typeof envelope.seq, "string");
  assert.equal(envelope.seq, SEQ);
  assert.notEqual(Number(envelope.seq).toString(), SEQ);
  assert.equal(envelope.op, "dispatch");
  assert.equal(envelope.event, "MESSAGE_CREATE");
  assert.equal(envelope.data.kind, "text");
  assert.equal(JSON.parse(JSON.stringify(envelope)).seq, SEQ);
});

test("voice-state fixture keeps the same seq encoding", () => {
  const envelope = JSON.parse(readFixture("voice-state.json"));
  assert.equal(envelope.event, "VOICE_STATE_UPDATE");
  assert.equal(typeof envelope.seq, "string");
  assert.equal(envelope.seq, SEQ);
});

test("rtc-token fixture is a closed contract shape", () => {
  const token = JSON.parse(readFixture("rtc-token.json"));
  assert.equal(typeof token.token, "string");
  assert.match(token.url, /^wss?:\/\//);
  assert.equal(typeof token.room, "string");
  assert.equal(typeof token.expires_at, "string");
});
