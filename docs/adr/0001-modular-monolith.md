# ADR 0001 — 模块化单体与平台栈

Status: Accepted · 2026-09-20

## Context

既需要长期模块边界，又不能为尚无规模的业务引入微服务运维。

## Decision

客户端 Avalonia/.NET，业务模块独立 csproj；服务端 Rust/Tokio/Axum，以 domain/protocol/app crate 划清依赖；媒体 C++。

组合根显式装配，模块私有实现默认 internal；service 持有 repository 端口。Postgres、Redis、S3、LiveKit 为独立基础设施。

## Consequences

多语言有构建成本，因此固定依赖锁文件并做三平台 CI。暂不创建 Auth/Presence 等空服务；有业务实现时再增模块。Native AOT 留作发布验证，不作为未验证的内存承诺。
