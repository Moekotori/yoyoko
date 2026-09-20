# ADR 0002 — 实例隔离与缓存事实源

Status: Accepted · 2026-09-20

## Context

全局 CurrentUser 会使自托管与跨实例账号混淆。

## Decision

一个客户端连接多个独立实例，每实例独立身份、账号、连接和数据；不实现 Federation。

InstanceContext 拥有生命周期，EntityKey 加 InstanceId，私有 SQLite 数据额外按 AccountId 分区。服务器为事实源，cache-first UI 后台增量同步。

## Consequences

注销与撤权需要显式清理私有缓存。每实例当前只一个活动账号；令牌走 OS vault，不存 SQLite。缓存不是可信权限证明。
