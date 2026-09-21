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

test("user-update fixture carries an optional animated avatar", () => {
  const envelope = JSON.parse(readFixture("user-update.json"));
  assert.equal(envelope.event, "USER_UPDATE");
  assert.equal(typeof envelope.seq, "string");
  assert.equal(envelope.data.username, "ada");
  assert.equal(envelope.data.avatar.animated, true);
  assert.match(envelope.data.avatar.mime_type, /^image\//);
});

test("server fixture carries optional moderation fields", () => {
  const server = JSON.parse(readFixture("server.json"));
  assert.equal(server.cooldown_seconds, 5);
  assert.deepEqual(server.blocked_words, ["spam"]);
  assert.match(server.id, /^[0-9a-f-]{36}$/);
});

test("message-update fixture keeps seq encoding and edited_at", () => {
  const envelope = JSON.parse(readFixture("message-update.json"));
  assert.equal(envelope.event, "MESSAGE_UPDATE");
  assert.equal(typeof envelope.seq, "string");
  assert.equal(envelope.data.content, "Hello, edited");
  assert.equal(typeof envelope.data.edited_at, "string");
});

test("rtc-token fixture is a closed contract shape", () => {
  const token = JSON.parse(readFixture("rtc-token.json"));
  assert.equal(typeof token.token, "string");
  assert.match(token.url, /^wss?:\/\//);
  assert.equal(typeof token.room, "string");
  assert.equal(typeof token.expires_at, "string");
});
