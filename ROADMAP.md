# Roadmap

## Phase 0 — 当前交付

独立客户端模块、Rust 模块化单体、Native C ABI、实例模型、共享协议样本、SQLite 端口和初始实现、PostgreSQL schema、配置、Compose、CI、ADR。运行与平台验证范围见 VALIDATION。

## Phase 1 — 进行中：两个真实客户端聊天

已纵向接通 1–6 的可运行切片：Auth、社区/频道、消息、图片附件、Gateway READY/heartbeat/resume、SQLite 同步、虚拟化时间线。社区可配置屏蔽词与发言冷却。本机默认 `local:` 存储；Postgres 适配与 migration 保留给 Compose/CI。

尚未完成：

- 系统凭据库（Keychain / Credential Manager）；当前 refresh 为 0600 文件
- 双窗口 GUI 验收、断网恢复与 100/1000/10000 条负载测量
- 编辑/删除 UI、权限撤销后的离线内容禁用
- 生产 S3 上传（当前本地对象目录）

不把 Phase 0 的 URL 添加视为完成 Phase 1；上线前还需真实认证、安全限流、依赖 readiness 和日志脱敏验证。

## 后续阶段

| 阶段 | 内容与边界 |
| --- | --- |
| 2 | 编辑、删除、回复、mention、reaction、通知、typing、presence；虚拟化与全局缓存预算验收。附件上传/下载与可配置大小上限已提前落地 |
| 3 | 完整角色管理、频道 override、moderation、kick/ban；基础授权早在 Phase 1 生效 |
| 4 | 语音控制面已落地（token、状态、mute/deafen、频道音质上限与用户自选档位、隔离 worker）。剩余：链接 LiveKit 客户端、设备选择、按档位编码 Opus/AEC/RNNoise、真实双端听筒验收；不将 native crash 放进 UI |
| 5 | 三平台 capture、H.264 硬件编码、零拷贝路径优先、1080p30/60 与 2–3 帧队列 |
| 6 | 自托管生产安装器、单一业务配置衍生基础设施配置、TLS、TURN、MinIO bucket、安全默认值、迁移/备份/恢复 |
| 7 | 媒体进程协议、资源回收、重启、崩溃恢复与分发硬化；崩溃隔离不推迟到此阶段才处理 |
| 8 | 独立 Bot token、Bot REST/Gateway、签名 webhook；不允许任意插件进入 server 进程 |
| 9 | Mobile/Web 协议兼容审查，不开始移动端 UI |
| 10 | Federation、E2EE、AV1、插件、Threads 等分别立项 |

Phase 0 不承诺闲置 50–100 MiB 已达标；后续以可复现真实窗口和工作负载作为性能门槛。
