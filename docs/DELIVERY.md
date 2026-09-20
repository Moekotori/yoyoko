# Phase 0 deliverables

本次需求要求的 19 项输出对应如下。状态及验证限制以 [VALIDATION.md](VALIDATION.md) 为准。

| 输出 | 位置 |
| --- | --- |
| 1. 新目录结构 | [Architecture / Monorepo](../ARCHITECTURE.md#monorepo) |
| 2. 每个模块职责 | [Architecture](../ARCHITECTURE.md) |
| 3. Client 架构 | [Client](../ARCHITECTURE.md#client) 与独立 csproj |
| 4. Server 架构 | [Server](../ARCHITECTURE.md#server) 与独立 Rust crates |
| 5. Native Media 架构 | [Native media](../ARCHITECTURE.md#native-media)、[ABI](../native-media-core/README.md) |
| 6. Protocol 设计 | [Protocol v1](protocol/README.md) 与共享 JSON fixtures |
| 7. Multi-Instance 设计 | [Multi-instance](../ARCHITECTURE.md#multi-instance) |
| 8. Self-host 设计 | [SELF_HOSTING.md](../SELF_HOSTING.md) |
| 9. SQLite 设计 | [DATABASE.md](DATABASE.md) 与 cache migration |
| 10. PostgreSQL 初步 Schema | [0001_initial.sql](../database/migrations/0001_initial.sql) |
| 11. Gateway 设计 | [Protocol v1 / Gateway](protocol/README.md#gateway) |
| 12. Docker 架构 | [compose.yaml](../compose.yaml)、[Dockerfile](../deploy/Dockerfile) |
| 13. CI 架构 | [ci.yml](../.github/workflows/ci.yml)、[scripts/ci.mjs](../scripts/ci.mjs)、[tests/node](../tests/node)、[Development](../DEVELOPMENT.md#ci) |
| 14. ADR 文档 | [5 份 ADR](adr/README.md) |
| 15. DEVELOPMENT.md | [开发指南](../DEVELOPMENT.md) |
| 16. ARCHITECTURE.md | [架构文档](../ARCHITECTURE.md) |
| 17. ROADMAP.md | [路线图](../ROADMAP.md) |
| 18. SELF_HOSTING.md | [自托管指南](../SELF_HOSTING.md) |
| 19. 下一阶段执行计划 | [Phase 1 纵向实现与验收](../ROADMAP.md#phase-1--下一阶段两个真实客户端聊天) |
