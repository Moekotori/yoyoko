# ADR 0004 — 原生边界与崩溃隔离

Status: Accepted · 2026-09-20

## Context

高频 PCM / GPU frames 进 C# 会增加复制和 GC；进程内异常捕获无法隔离原生崩溃。

## Decision

稳定 C ABI，只传控制参数；媒体始终保留可放入 worker 的边界，idle 不加载。

当前 native 操作返回 Not implemented yet；在接真实媒体前建立 worker，UI 仅 control IPC 与低频状态。

## Consequences

比最简单进程内桥接增加少量初始成本，但满足“不允许 native crash 拖死客户端”。具体 IPC 传输待媒体阶段确定，不先造空 IPC 框架。
