# ADR 0001 — 模块化单体与平台栈

Status: Accepted · 2026-09-20

## Context

既需要长期模块边界，又不能为尚无规模的业务引入微服务运维。客户端与服务端同仓便于协议共进化，但服务端最终要能整包分离为独立产品。

## Decision

客户端 Avalonia/.NET，业务模块独立 csproj；服务端 Rust/Tokio/Axum，以 domain/protocol/app crate 划清依赖；媒体 C++。

组合根显式装配，模块私有实现默认 internal；service 持有 repository 端口。Postgres、Redis、S3、LiveKit 为独立基础设施。

跨端只通过版本化 HTTP/Gateway 协议与共享 fixtures 交互，不共享领域类型、进程或实现文件。同仓是过渡；分离指抽出整个服务端仓库，不把模块化单体拆成业务微服务。

## Consequences

多语言有构建成本，因此固定依赖锁文件并做三平台 CI。暂不创建 Auth/Presence 等空服务；有业务实现时再增模块，且按文件职责拆开，避免万能 `api`/`services`/`store`。Native AOT 留作发布验证，不作为未验证的内存承诺。新依赖不得挡住日后抽出 `src/server`。
