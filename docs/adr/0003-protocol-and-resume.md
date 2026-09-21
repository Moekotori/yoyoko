# ADR 0003 — 版本协议与恢复

Status: Accepted · 2026-09-20

## Context

需要支持未来 Web/移动端且不依赖 Discord 格式，不能每次断线下载全部状态。

## Decision

REST 负责请求，Gateway 负责事件；独立 versioned JSON DTO、UUIDv7、字符串 64 位计数器。

session seq + SQLite 原子提交 cursor + 服务端有限 replay；过期 session 明确 resync，禁用无界历史内存缓存。

## Consequences

客户端已按该决策提交 cursor、忽略重复 seq、缺口后 resume，并在 session 失效时 identify + 当前频道一页同步。维护双语言 DTO，使用共享 fixture 约束漂移，未来扩展更完整 schema。
