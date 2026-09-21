# Phase 0 validation

2026-09-20 · macOS 27.0 / Apple Silicon · .NET SDK 10.0.401 / runtime 10.0.12 · Avalonia 12.1.2 · Rust 1.98.1 · AppleClang 21 / CMake 4.4.2。

## 本机通过

- `dotnet build Chat.sln -c Release`：9 个项目，0 warning / 0 error。
- Client contract suite：15 项；真实本地 Gateway 联通检查另 2 项，共 17 项通过。
- Node 协议管线：`npm test` 4 项 fixtures；`npm run ci` 另 5 项 live（health / discovery / 业务路由关闭 / Gateway 4406 与 4401）通过。未跑远程 GitHub Actions。
- `cargo check`、`cargo test --workspace --locked`：3 个聚焦 Rust 测试通过。
- `cargo clippy --workspace --all-targets --locked -- -D warnings`：通过。
- CMake Release build + C 语言 ABI consumer：通过；未实现的语音/共享调用明确返回 `MEDIA_NOT_IMPLEMENTED`。
- C# whitespace / Rust fmt / client 项目依赖白名单：通过。
- 本机 Rust 进程监听 loopback:8080；HTTP health 与 discovery 返回正确 JSON。
- TOML 配置与环境变量覆盖（字符串、数字、origin 数组）通过 `check-config`。
- Avalonia 实际窗口检查：三栏可见，无复杂卡片；输入 localhost 地址并添加，实例名显示为 Local community，成功消息可见。
- 停止本地 Rust 服务后重新打开实际窗口：SQLite 实例仍显示，无需等待网络。
- 使用实际 C# WebSocket transport 读取 Rust HELLO；发送不兼容版本后服务端关闭连接。

截图：`artifacts/phase0-desktop.png`，为本次开发版实际窗口，不是设计稿。截图和性能 JSON 属于忽略的本机输出，不进入源码。

## 启动说明

本机生成的默认 .NET apphost 两次被系统终止（exit 137），没有据此断言根因。使用 `dotnet <assembly.dll>` 可以正常启动。App 项目设置 `UseAppHost=false` 后，`dotnet run --project src/client/App -c Release --no-build` 已实际启动并保持运行。

为了让 macOS UI 检查工具定位窗口，检查过程中在 artifacts 下构建过只用于本机的开发 .app（使用已安装 hostfxr 运行相同 Release assemblies）。它不是分发包，不代表签名、公证或打包验收，不能跨机器分发。正常开发入口仍是上述 dotnet run。

## 内存结果与未达目标

相同 Release UI 在交互后的本机开发 host 中做 5 次一秒采样，RSS **184.22–184.27 MiB**，`ps %CPU` 显示 **0.0–0.2%**。数据见 `artifacts/idle-phase0.json`。这是单进程 RSS，窗口可见、一个已保存实例、无聊天记录、无媒体进程，且已执行 UI automation。`ps %CPU` 不是精确采样区间 CPU 利用率。

较早直接启动的进程 RSS 约 170 MiB；dotnet run 新启动时也观察到约 203 MiB。只把 184 MiB 的上述受记录场景作为当前粗略基线，不用不同宿主/生命周期的瞬时值宣称优化成果。

**50–100 MiB 目标未达成。** 当前基线低于需求中明确避免的 300–500 MiB，但不能据此承诺长期低内存。下一步先拆分 Avalonia/Skia、字体、JIT/GC 的实际占用，比较常规发布与可用的裁剪/AOT，达到可解释基线后再引入真实消息负载。没有完整时间线，因此没有伪造 100/1000/10000 messages、render latency、切频道、RTC CPU 的数据。

## 尚未验证 / 尚未实现

- 2026-09-20 开发机无 Docker。2026-09-21 Windows 10 已用 Docker Desktop 29.8 / Compose v5.5.1 执行 `docker compose up -d --build`：镜像 `chat-api:local` 构建成功，migrate 退出 0，api/postgres/redis/livekit 健康；`/health/ready`、`/.well-known/lightchat`、5 项 Node live、`POST /auth/register` 与 `POST /servers` 通过。附件仍在 API 本地卷；MinIO 不在默认堆栈（Docker Hub 社区镜像已下架，业务未接入）。公网 TLS / 远端部署未测。
- GitHub Actions 已配置，尚未运行远程工作流；不能声称 Windows/Linux 构建或 GUI 验收已通过。
- Redis 目前只给 Compose 里的 LiveKit 用；聊天进程未连 Redis。鉴权/频道/@ 用户名用进程内有界 TTL 缓存，消息仍以 Store 为事实源。MinIO 为可选 profile，附件仍写本地卷。
- 屏幕共享、系统凭据库、AOT、安装包分发：Not implemented yet。Auth / 消息 / Gateway resume 已在 Phase 1 接通，见正文与协议文档。
- 原生仅验证控制 ABI，无真实设备、音频、GPU 捕获或硬件编解码结果。
- 本次无 SSH、无公网部署、无外部服务改动。
