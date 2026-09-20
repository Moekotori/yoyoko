# LightChat

面向 **Windows、macOS、Linux** 的轻量聊天软件：保留类似 Discord 的核心使用体验，优先低内存、快速启动和流畅响应，同时允许用户自托管完整服务端。

LightChat 是暂用名，显示名称由配置决定，代码模块使用中性的 `Chat` / `chat` 命名。

## 当前状态

当前处于 **Phase 1：两个真实客户端文字聊天**（含发图）。Phase 0 架构边界保留。

| 能力 | 状态 |
| --- | --- |
| Avalonia 桌面外壳、轻量 MVVM、独立客户端项目 | 已实现 |
| 登录 / 注册表单 | 独立页签、居中等宽输入、密码框回车提交；视觉验收状态见 [UI 说明](docs/ui/README.md#authentication-form) |
| 设置页面 | 通用 / 外观 / 资料分类、语言和发送方式单选、布局与动效开关；[离屏渲染与验证记录](docs/ui/README.md#settings) |
| 客户端界面语言（中文 / English / 日本語），设置中切换 | 已实现；跟随系统，可覆盖并写入本地偏好 |
| 实例发现、添加、SQLite 保存及离线恢复 | 已实现并在本机窗口验证 |
| 独立 InstanceContext、账号/实例隔离的数据契约 | 已实现；登录会话按实例隔离 |
| SQLite 消息分页缓存与账号隔离 | 已实现并接入远程同步 |
| Rust 健康检查、发现、认证 Gateway READY | 已实现 |
| Domain、Service、Repository | 已实现 VIEW_CHANNEL / SEND_MESSAGE 服务端检查 |
| C++ Native Media C ABI | 骨架可编译，能力位为 0，媒体操作明确返回未实现 |
| PostgreSQL migration、Docker Compose、CI | 本地 Node 管线已接通；容器/迁移及远程 GitHub Actions 运行尚未验收 |
| 注册、登录、社区/频道、文字消息 | 已接通控制面；完整验收见路线图 |
| 社区屏蔽词与发言冷却 | 已实现；owner / MANAGE_MESSAGES 可 PATCH，发送时服务端强制 |
| 聊天附件（拖拽/选择图片与文件、可配置上限、对端下载） | 已实现；默认 24 MiB，服务端 `storage.max_bytes` 可改 |
| 语音频道加入/离开、mute/deafen、LiveKit token、音质档位 | 已实现控制面（含 384/510 kbps 高音质选择）；真实麦克风/听筒需 LiveKit 与媒体 worker |
| 屏幕共享 | **Not implemented yet** |

本机已通过 .NET Release 构建、Rust 聚焦测试与 Clippy、Native C ABI 检查，并验证了实例添加和离线恢复。Windows/Linux 的实际构建、GUI、安装包及媒体硬件尚未验证。

低内存是架构要求，闲置目标尽量控制在 **50–100 MB**。当前 macOS Release 基础窗口一次记录的 RSS 约 **184 MiB**，**尚未达标**；这不是聊天、语音或共享场景的性能结果。测量条件与限制见 [验证记录](docs/VALIDATION.md)。

## 产品目标

- 文字聊天：频道、历史、回复、引用、编辑、删除、mention、reaction、表情、文件和图片。
- 社区协作：多个社区和频道、角色与权限、成员管理、通知、typing 和 presence。
- 实时媒体：语音频道、设备选择、静音/耳聋、降噪，以及三平台屏幕共享。
- 自托管：账号、社区、频道、消息、文件和 RTC 均由各实例独立管理；官方只是使用同一协议的托管实例。
- 长期扩展：保留 Bot、Webhook、未来 E2EE、移动端和 Web 的协议边界，但不提前实现 Federation、任意插件或自研 SFU。

以上是产品方向，不代表当前版本已经提供这些功能。

## 多实例优先

客户端从第一天就支持添加多个独立地址：

```text
Desktop Client
 ├─ https://chat.example.com
 ├─ https://friends.example.net
 └─ https://company.internal
```

每个实例有自己的账号、管理员和数据。相同用户名在不同实例之间没有身份关联，实例之间也不通信；这与 Federation 是两回事。

`InstanceManager` 管理多个 `InstanceContext`，每个上下文拥有独立账号、连接与缓存作用域。远端实体包含 InstanceId，本地私有消息缓存额外包含 AccountId，避免跨实例或跨账号串数据。

用户只需输入域名，通过 `/.well-known/lightchat` 发现 API、Gateway、CDN 和 RTC 地址。发现路径是协议标识，不随产品改名。默认使用 HTTPS/WSS，本机开发允许回环 HTTP/WS。

## 技术栈与边界

| 部分 | 技术 | 主要职责 |
| --- | --- | --- |
| 桌面 UI | Avalonia + C#/.NET | 简洁布局、轻量 MVVM、编译绑定 |
| 应用核心 | C# | 实例、账号、消息、权限和生命周期编排 |
| 本地缓存 | SQLite | 离线读取、分页、增量同步落地 |
| 网络 | HTTP + WebSocket | HTTP 业务请求、Gateway 实时事件 |
| 服务端 | Rust + Tokio + Axum | 模块化单体、鉴权、业务服务与 Repository |
| 数据库 | PostgreSQL | 业务事实源、版本化迁移、初期全文检索 |
| 实时状态 | Redis | Presence、短期会话及语音状态 |
| 对象存储 | S3 Compatible / MinIO | 文件、图片及缩略图 |
| RTC | WebRTC / LiveKit | 独立于聊天 Backend 的媒体传输 |
| Native Media | C++ + C ABI | 音频处理、捕获、硬件编解码与平台能力 |

服务端先采用模块化单体，不提前拆微服务。同仓是过渡：跨端只走协议，服务端必须能日后整包分离为独立仓库，不依赖桌面客户端源码或进程。客户端以独立工程明确边界，业务不写进窗口，也不直接依赖数据库实现。

```text
App 组合根
 ├─ UI → Core → Domain / Protocol
 ├─ Networking → Core / Protocol
 ├─ Storage → Core / Domain / Protocol
 └─ Media → 按需媒体 worker（后续接入）

Client → HTTP / Gateway → Rust API → Service → Repository → PostgreSQL
Client → WebRTC → LiveKit SFU
```

缓存读取遵循 `Remote → Sync → SQLite → UI`，启动先展示本地状态，再后台连接。媒体原始数据不经 C#，优先 GPU 捕获直达硬件编码器，队列保留 2–3 帧；真实媒体接入前保证原生崩溃不会拖垮主界面。

## 目录与模块

```text
src/client/
  App/          组合入口、配置、启动与退出
  Domain/       实例、账号、社区、频道、消息、角色、权限
  Protocol/     独立 DTO、协议版本与 JSON 序列化
  Core/         业务核心、状态所有权、网络/缓存端口
  Networking/   HTTP discovery 与 WebSocket transport
  Storage/      SQLite、migration 与消息分页缓存
  Media/        媒体接口、能力声明、Native Bridge
  UI/           Shell、Instances、Channels、Components、Styles
src/server/
  domain/       独立领域 crate
  protocol/     独立协议 crate
  app/          API、Gateway、配置、service/repository/adapter
native-media-core/
  ffi/          C ABI 与句柄生命周期
  audio/ video/ capture/ codec/ rtc/ platform/
                后续原生模块边界，明确标记未实现
database/migrations/ PostgreSQL 版本化 schema
tests/client/        客户端契约与缓存聚焦检查
tests/node/          Node 协议 fixtures 与 live HTTP/Gateway 检查
deploy/              Dockerfile 与基础设施配置
scripts/             本地 CI 入口、模块边界检查与进程采样
benchmarks/          性能记录方法
docs/                协议、数据库、ADR 与验证记录
.github/workflows/   CI
```

## 快速开始

macOS 上双击仓库根目录的 `start.command`，或在终端运行 `./start.command`，即可构建并打开桌面 UI。脚本会启动本地服务端；若 `http://localhost:8080` 已有可用的聊天服务则复用。关闭客户端时只停止脚本自己启动的服务。首次启动需要安装 .NET SDK 10 和 Rust，且需要联网下载构建依赖。

调 UI 时双击 `dev.command`（或运行 `./dev.command`）：使用 Debug 模式，保存 `.axaml` 后由 HotAvalonia 实时重载；C# 修改由 `dotnet watch` 热重载，无法应用时自动重启客户端。按 Ctrl+C 停止开发模式。Rust 服务端不随客户端修改重启。具体限制见 [开发热重载](DEVELOPMENT.md#开发热重载)。

需要 .NET SDK 10、Rust stable ≥ 1.90。构建原生骨架另需 CMake ≥ 3.24 和 C++20 编译器。协议 fixtures 与对本机 API 的 live 检查需要 Node.js ≥ 22（无 npm 依赖）。以下命令均在仓库根目录执行。

终端一，启动本地服务端：

```sh
cargo run --locked -p chat-server
```

终端二，启动桌面客户端：

```sh
dotnet restore Chat.sln --locked-mode
dotnet run --project src/client/App -c Release
```

在客户端输入 `http://localhost:8080`，添加实例后注册或登录。创建社区会得到邀请码；第二个客户端用同一地址登录并加入，即可互发文字和图片。本机 `cargo run` 默认使用 `local:data/chat.json` 与 `data/objects`，不需要 PostgreSQL、Docker 或 MinIO。两个客户端请用不同的 `CHAT_CACHE_DIRECTORY`，避免抢同一份 SQLite。

```sh
CHAT_CACHE_DIRECTORY=/tmp/chat-b dotnet run --project src/client/App -c Release
```

同一局域网里的其他设备请用 `npm run dev` 启动服务，再在客户端输入打印出的 `http://192.168.x.x:8080`。默认 `cargo run` 只绑回环地址，局域网连不上。`npm run lan` 与 `dev` 相同。

App 开发工程使用 `UseAppHost=false`，由已安装的 dotnet runtime 启动；独立安装包与签名发布属于后续工作。

### 配置

- 服务端：`config.toml`；通过 `CHAT_CONFIG` 选择其他配置文件，所有字段可用 `CHAT__SECTION__KEY` 覆盖。
- 客户端：`src/client/App/appsettings.json`；支持 `CHAT_PRODUCT_NAME` 和 `CHAT_CACHE_DIRECTORY`。
- 每个部署的实例使用稳定且唯一的 InstanceId；重启、升级与恢复时保留。
- 切勿把真实密码、token 或 SSH 凭据写进配置样例、日志或仓库。刷新令牌将接入系统凭据库。

```sh
cargo run --locked -p chat-server -- check-config
```

## 自托管基础

已准备 API/Gateway、PostgreSQL、Redis、MinIO、LiveKit 和一次性 migration 的 Compose 环境：

```sh
docker compose up -d --build
```

当前模板用于本地开发，端口绑定回环地址，包含开发凭据；尚未完成公网 TLS、生产身份认证、MinIO bucket 自动配置或真实 RTC 接线，也未在本机运行 Docker 验证。完整生产自托管将在后续阶段完善，具体配置、升级与备份见 [SELF_HOSTING.md](SELF_HOSTING.md)。

## 开发原则与必要检查

- 文件与模块按职责拆分，明确依赖和状态所有者，不堆进单个窗口、ViewModel 或万能服务；服务端同样按能力拆 handler / service / adapter，为日后整包分离服务端做准备。
- UI 从简，通过文字、间距和对齐建立层级；避免滥用圆角、卡片、边框、阴影和辅助小字。
- 消息列表必须分页和虚拟化，图片缩略图优先，网络与缓存有界，媒体按需启动。
- 权限在服务端验证，官方与自托管遵循同一协议；未实现的能力明确失败，不伪造成功。
- 不过度测试：只执行本次改动相关的必要构建、聚焦检查和适当的实际运行验证。纯文档修改无需重跑工程。

按所改模块选用以下命令，不要求每次全部执行：

```sh
# 协议 fixtures 与本机 API/Gateway（Node ≥ 22）
npm test
npm run test:live
npm run ci

# 客户端构建、契约与缓存边界
dotnet build Chat.sln -c Release
dotnet run --project tests/client -c Release
python3 scripts/check_boundaries.py

# 服务端
cargo test --workspace --locked
cargo clippy --workspace --all-targets --locked -- -D warnings

# Native ABI
cmake -S native-media-core -B build/native -DCMAKE_BUILD_TYPE=Release
cmake --build build/native --config Release
ctest --test-dir build/native -C Release --output-on-failure
```

完整工程约定见 [AGENTS.md](AGENTS.md)，格式化、CI 与开发说明见 [DEVELOPMENT.md](DEVELOPMENT.md)。

## 阶段规划

| 阶段 | 目标 |
| --- | --- |
| 0 · 当前 | 架构、工程、协议、领域模型、存储/网络/媒体边界、配置、CI、文档 |
| 1 | 实例账号、注册登录、社区/频道、发送接收、SQLite 同步、Gateway 重连；两个客户端真实聊天 |
| 2 | 编辑删除、回复、mention/reaction、附件图片、通知、typing/presence、时间线性能验收 |
| 3 | 完整角色管理、频道 override、moderation、kick/ban；基础授权从 Phase 1 开始 |
| 4–5 | 真实语音、原生处理与三平台共享；启用媒体前落实崩溃隔离 |
| 6–7 | 生产自托管、备份升级，以及媒体进程恢复、资源回收与分发完善 |
| 8–9 | Bot/Webhook、移动端/Web 协议审查 |
| 10 | Federation、E2EE、AV1、插件等后续独立规划 |

下一阶段先接通 `Auth → Server/Channel → Message → Gateway → SQLite Sync → Chat UI`，验收多实例隔离、两个客户端互发、断线恢复和离线启动。具体顺序与验收条件见 [ROADMAP.md](ROADMAP.md)。

## 文档导航

| 文档 | 内容 |
| --- | --- |
| [AGENTS.md](AGENTS.md) | 后续开发必须遵循的工程、UI、性能与协作约定 |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 模块职责、依赖方向、客户端/服务端/媒体与内存预算 |
| [DEVELOPMENT.md](DEVELOPMENT.md) | 环境、构建、运行、配置、格式、CI |
| [ROADMAP.md](ROADMAP.md) | 阶段边界与下一阶段计划 |
| [SELF_HOSTING.md](SELF_HOSTING.md) | 部署基础、配置、升级与备份 |
| [Protocol v1](docs/protocol/README.md) | HTTP/Gateway、版本、消息格式与恢复设计 |
| [数据库设计](docs/DATABASE.md) | PostgreSQL schema、SQLite 作用域与迁移 |
| [ADR](docs/adr/README.md) | 架构决策及其取舍 |
| [性能测量](benchmarks/README.md) | 性能指标与复现条件 |
| [验证记录](docs/VALIDATION.md) | 实测结果、未达目标和未验证范围 |
| [Phase 0 交付索引](docs/DELIVERY.md) | 原始需求的 19 项输出对应位置 |
