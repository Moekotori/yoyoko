# Roadmap

## Phase 0 — 当前交付

独立客户端模块、Rust 模块化单体、Native C ABI、实例模型、共享协议样本、SQLite 端口和初始实现、PostgreSQL schema、配置、Compose、CI、ADR。运行与平台验证范围见 VALIDATION。

## Phase 1 — 下一阶段：两个真实客户端聊天

按以下顺序纵向接通，不先铺满 UI：

1. Auth：Argon2id、每实例用户、短期 access + 单次轮换 refresh、服务端速率限制、系统凭据库。完成注册/登录/退出，匿名请求永远不能进入业务服务。
2. Server/Channel：创建社区、成员与 base role、创建文字频道。通过 service/repository 端口接 PostgreSQL；最基本 VIEW_CHANNEL / SEND_MESSAGE 授权随第一条业务 API 一起实现，不能等 Phase 3 才防越权。
3. Message：UUIDv7、幂等发送、最近 50 条 keyset page；写消息 + outbox 同一事务。扩展本地 server/channel/user cache migration。
4. Gateway：认证 READY、独立 session seq、heartbeat、有限 replay 与 invalid_session、指数退避和取消。每实例独立连接；后台读取只在登录后启动。
5. Sync：网络事件与 cursor 原子写 SQLite，再由 UI 读缓存。实现 edit/delete 占位事件处理策略、权限撤销清理，禁用失权离线内容。
6. Chat UI：真实数据、虚拟化与最多 300 条模型窗口、分页、草稿、发送中/失败/重试状态；不加载整个频道。
7. 验收：两个客户端同一实例互发；一个客户端同时登录两个独立实例；断网恢复不重复/不漏消息；重启离线读取；账号切换与越权请求被隔离。用 100/1000/10000 条服务端数据测窗口、缓存、内存与切频道延迟。

不把 Phase 0 的 URL 添加视为完成 Phase 1；上线前还需真实认证、安全限流、依赖 readiness 和日志脱敏验证。

## 后续阶段

| 阶段 | 内容与边界 |
| --- | --- |
| 2 | 编辑、删除、回复、mention、reaction、附件、缩略图、通知、typing、presence；虚拟化与全局缓存预算验收 |
| 3 | 完整角色管理、频道 override、moderation、kick/ban；基础授权早在 Phase 1 生效 |
| 4 | LiveKit 真实媒体与按需 worker 接线；Opus/AEC/RNNoise/AGC/VAD、设备选择、mute/deafen；不将 native crash 放进 UI |
| 5 | 三平台 capture、H.264 硬件编码、零拷贝路径优先、1080p30/60 与 2–3 帧队列 |
| 6 | 自托管生产安装器、单一业务配置衍生基础设施配置、TLS、TURN、MinIO bucket、安全默认值、迁移/备份/恢复 |
| 7 | 媒体进程协议、资源回收、重启、崩溃恢复与分发硬化；崩溃隔离不推迟到此阶段才处理 |
| 8 | 独立 Bot token、Bot REST/Gateway、签名 webhook；不允许任意插件进入 server 进程 |
| 9 | Mobile/Web 协议兼容审查，不开始移动端 UI |
| 10 | Federation、E2EE、AV1、插件、Threads 等分别立项 |

Phase 0 不承诺闲置 50–100 MiB 已达标；后续以可复现真实窗口和工作负载作为性能门槛。
