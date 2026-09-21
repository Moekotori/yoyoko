# yoyoko

面向 **Windows、macOS、Linux** 的轻量聊天软件：保留类似 Discord 的核心使用体验，优先低内存、快速启动和流畅响应，同时允许用户自托管完整服务端。

产品名为 yoyoko，显示名称由配置决定，代码模块使用中性的 `Chat` / `chat` 命名。

## 当前状态

当前处于 **Phase 1：两个真实客户端文字聊天**（含发图）。Phase 0 架构边界保留。

| 能力 | 状态 |
| --- | --- |
| Avalonia 桌面外壳、轻量 MVVM、独立客户端项目 | 已实现 |
| 轻量动画系统 `Chat.Motion` | 已实现入场/切换预设、可中断动画、隐藏/最小化清理与减少动效；文字频道仅消息区轻微淡入，过期加载取消；频道分组与社区菜单分区用 `RevealHost` 做可中断收展；[调用与验证](docs/ui/MOTION.md) |
| 自动连接 / 账号设置 | 首次自动创建实例账号，之后恢复会话；设置 → 资料修改头像、横幅、显示名和用户名；点击底部账号也可改这些项；设置中更换服务器并记住选择 |
| 设置页面 | 左下角入口，打开时频道列收起并把空间留给设置，分类导航贴着实例栏；点左侧实例或 Escape 可返回工作区；分类含通用、外观、快捷键、语音、资料和服务器；语言 / 发送快捷键 / 可改绑定的键盘快捷键 / 设备、深色/浅色、自定义背景、紧凑布局与动效开关；[界面验证记录](docs/ui/README.md#settings) |
| 界面细节 | 统一圆角选择框与输入框、简化设置分隔、社区操作按需展开；[本轮视觉与验证](docs/ui/README.md#product-surface-polish-2026-09-21) |
| 工作区快捷键 | 跳转频道、搜索、频道/标签切换、设置、成员栏、附件与语音 mute/deafen；设置中可改绑定；[快捷键](docs/ui/README.md#keyboard) |
| 客户端界面语言（中文 / English / 日本語），设置中切换 | 已实现；跟随系统，可覆盖并写入本地偏好 |
| 实例发现、添加、SQLite 保存及离线恢复 | 已实现并在本机窗口验证 |
| 独立 InstanceContext、账号/实例隔离的数据契约 | 已实现；登录会话按实例隔离 |
| SQLite 消息分页缓存与账号隔离 | 已实现并接入远程同步；未发送文字写入有界 outbox |
| 乐观发送与弱网恢复 | 本地先上屏、失败可点重试/取消；Gateway 去重、缺口 resume、`invalid_session` 后当前频道一页同步 |
| 界面内存调度 | 已接入前台 / 空闲 / 压力 / 隐藏四档；设置 → 常规可开启深度 UltraLight，最小化/隐藏时卸载页面，保留消息和语音会话，恢复失败可重试；[策略与验证](docs/MEMORY_SCHEDULING.md) |
| Rust 健康检查、发现、认证 Gateway READY | 已实现 |
| Domain、Service、Repository | 已实现 VIEW_CHANNEL / SEND_MESSAGE 服务端检查；有界 TTL 热缓存，不缓存消息正文 |
| C++ Native Media C ABI | 骨架可编译，能力位为 0，媒体操作明确返回未实现 |
| PostgreSQL migration、Docker Compose、CI | 本机一键堆栈为 `docker compose up -d --build`；自动 CI 仅轻量检查，Linux/全平台与容器 live 按需手动触发 |
| 注册、登录、社区/频道、文字消息 | 已接通控制面；完整验收见路线图 |
| 社区屏蔽词与发言冷却 | 已实现；owner / MANAGE_MESSAGES 可 PATCH，发送时服务端强制 |
| 聊天附件（拖拽/选择图片与文件、可配置上限、对端下载） | 已实现；默认 24 MiB，服务端 `storage.max_bytes` 可改 |
| 消息 Markdown / 轻量 LaTeX / 安全 HTML | 客户端渲染；协议仍为纯文本。粗体/斜体/删除线/代码/标题/列表/引用/简单表格/链接/`$...$` 与 `$$...$$`（含 pmatrix）。另支持有界 HTML 子集（`b`/`i`/`u`/`s`/`code`/`pre`/`a`/`p`/`br`/`h1-h3`/`ul`/`ol`/`li`/`blockquote`/`table` 与常见实体）。脚本、事件、`javascript:` 链接和远程 `<img>` 会被丢掉；`<img alt>` 只保留替代文字。明文快路径与有界缓存；简单公式压成 Unicode。不引入 WebView、不渲染远程 HTML 图片或完整 TeX |
| 未读 / @ / 静音、新消息分割线、按频道草稿、编辑/删除/回复、正在输入 | 客户端 SQLite 保存已读/通知档/草稿；作者可 PATCH 正文；作者或所有者可删除；发送可带 `reply_to`，被回复作者计入 mentions。`@username` / 显示名写入 mentions，`@everyone` 写入 `mention_everyone`，`@here` 写入 `mention_here` 并把当前 Gateway 在线成员（最多 256）写入 mentions。输入 `@` 可补全社区成员、@everyone 与 @here。时间线 `@用户` 可点开资料卡。输入走 `POST /channels/{id}/typing`，Gateway `TYPING_START` 不持久化。reaction / 系统通知尚未实现 |
| 用户浮卡、最近跳转、语音名册 | 点头像/名字打开浮卡可发私信或 @；⌘K 最近频道置顶并可搜人；成员栏右键可私信/提及/复制；语音频道下列出成员及静音/耳聋。说话光圈需真实媒体；服务端 presence **尚未实现** |
| 1:1 私信 | 同社区用户可打开或复用 `kind=dm` 频道，消息走现有发送/同步管道。频道列「私信」分组。无好友系统、群私信 |
| 语音频道加入/离开、mute/deafen、LiveKit token、音质档位、输入输出设备 | 控制面已接通；`chat-media-worker` 用 LiveKit Rust SDK 发布/订阅麦克风。局域网 `Host` 会改写 loopback RTC 地址；worker 退出或 LiveKit 断开后自动重连。说话状态来自 LiveKit 活跃说话人，侧栏头像 2 px 绿圈，不写业务库。要对端听到，需本机 LiveKit：完整堆栈用 `docker compose up -d --build`；只补媒体时 `docker compose up -d redis livekit`。Windows 无 Docker 时用 `.tools/livekit-server.exe --config deploy/livekit.host.yaml`（`start.cmd` 会自动拉起）。设置中可选耳机媒体键：仅在已加入语音时注册系统会话（Windows SMTC / macOS Now Playing / Linux MPRIS），播放/暂停映射 mute，默认关闭 |
| 网页进入语音 | 已实现：`{origin}/voice/{channel_id}` 只进该语音房。访客令牌不能列频道或读消息。桌面语音频道右键可复制链接。不是完整 Web 客户端 |
| 屏幕共享 | **Not implemented yet** |

本机已通过 .NET Release 构建、Rust 聚焦测试与 Clippy、Native C ABI 检查，并验证了实例添加和离线恢复。Windows 上 `docker compose up -d --build` 已实测：`/health/ready`、发现、5 项 Node live、Postgres 注册/建社区通过。Linux 构建、GUI、安装包及媒体硬件尚未验证。

低内存是架构要求，闲置目标尽量控制在 **50–100 MB**。当前 macOS Release 基础窗口一次记录的 RSS 约 **184 MiB**，**尚未达标**；这不是聊天、语音或共享场景的性能结果。测量条件与限制见 [验证记录](docs/VALIDATION.md)。2026-09-21 的独立空缓存窗口为约 **132 MiB RSS**，场景不同，不作为同条件优化幅度；详见 [内存调度记录](docs/MEMORY_SCHEDULING.md)。

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
  Motion/       轻量动画容器、预设与生命周期；仅依赖 Avalonia
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

服务端默认走 Docker。安装 Docker Desktop 后，在仓库根目录执行：

```sh
docker compose up -d --build
```

或双击 `up.cmd`（Windows）/ `up.command`（macOS）。首次构建镜像需要几分钟；就绪后访问 `http://localhost:8080/.well-known/lightchat`。停止：`docker compose down`（不加 `-v`，数据卷会保留）。

Windows 上双击 `start.cmd` 会先拉起这套 Compose（有 Docker 时），再打开桌面 UI。设置中输入 `localhost` 即连接 `http://localhost:8080`。没有 Docker 时回退到本机 `cargo` 进程。macOS 上 `start.command` 在 Docker 可用时同样先起服务端。Windows 首次启动若没有 .NET SDK 10，`start.cmd` 会下载到用户目录 `%USERPROFILE%\.dotnet`；之后仍需联网拉取构建依赖。

所有普通启动入口（含直接 `dotnet run`）都会连接默认服务器、恢复账号并选中文字频道；首次无账号时自动注册独立账号，用户名为「设置 → 服务器」中填写的值，未填则为 `User`；内网实例无社区时创建社区及默认 `general`。社区菜单可自由创建社区；社区拥有者可创建文字／语音频道，文字频道创建后自动打开，语音频道不会自动加入。真实数据保存在所连接服务器。首次默认地址集中在 `src/client/App/appsettings.json` 的 `default_instance_url`，可用 `CHAT_DEFAULT_INSTANCE_URL` 覆盖；在「设置 → 服务器」连接成功后优先使用记住的地址。已有账号会话失效时不会静默创建替代账号；设置 → 资料只编辑当前账号资料，不再提供登录/注册表单。

调 UI 时双击 `dev.cmd`（Windows）或 `dev.command`（macOS），或运行对应脚本：使用 Debug 模式，保存 `.axaml` 后由 HotAvalonia 实时重载；C# 修改由 `dotnet watch` 热重载，无法应用时自动重启客户端。按 Ctrl+C 停止开发模式。Rust 服务端不随客户端修改重启。具体限制见 [开发热重载](DEVELOPMENT.md#开发热重载)。

需要 .NET SDK 10、Rust stable ≥ 1.90。构建原生骨架另需 CMake ≥ 3.24 和 C++20 编译器。协议 fixtures 与对本机 API 的 live 检查需要 Node.js ≥ 22（无 npm 依赖）。以下命令均在仓库根目录执行。

改服务端源码、不想等镜像时，仍可用本机进程（`local:` 文件库，不经过 Postgres）：

```sh
cargo run --locked -p chat-server
```

终端二，连接上面单独启动的回环服务（覆盖默认局域网地址）：

```sh
dotnet restore Chat.sln --locked-mode
CHAT_CACHE_DIRECTORY=/tmp/chat-local CHAT_DEFAULT_INSTANCE_URL=http://localhost:8080 dotnet run --project src/client/App -c Release
```

输入 `localhost`（也支持 `http://localhost`）会连接配置的默认服务器，当前为 `http://localhost:8080`，无需输入端口。显式填写其他主机或带端口地址仍按其字面连接。此快捷地址仅在客户端内生效。

也可以通过「设置 → 服务器」输入其他服务器地址并连接；加号也打开这个设置入口。连接尚未保存账号的服务器时会提醒填写用户名，不填则注册为 `User`（该默认名已被占用时会改成 `User2` 等）。之后可在「设置 → 资料」修改用户名、显示名、头像和资料横幅。最左侧实例栏显示该实例账号的圆形头像，更换或移除后同步刷新；未设置时显示服务器名称首字。创建社区会得到邀请码；第二个客户端用同一地址登录并加入，即可互发文字和图片。本机 `cargo run` 默认使用 `local:data/chat.json` 与 `data/objects`，不需要 PostgreSQL、Docker 或 MinIO。两个客户端请用不同的 `CHAT_CACHE_DIRECTORY`，避免抢同一份 SQLite。

```sh
CHAT_CACHE_DIRECTORY=/tmp/chat-b dotnet run --project src/client/App -c Release
```

同一局域网里的其他设备请用 `npm run up -- --lan` 或 `npm run dev`。有 Docker 时会把 8080/7880 发到 `0.0.0.0`；没有 Docker 时 `dev`/`lan` 回退到本机 cargo。默认 `cargo run` 只绑回环地址。

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

本地/内网实例就是这一条：

```sh
docker compose up -d --build
```

会启动 API/Gateway、一次性 migration、PostgreSQL、Redis、LiveKit。聊天数据在 Postgres，附件在 API 容器卷（本地目录，不是 S3）。MinIO 社区镜像已离开 Docker Hub，且业务尚未接入；需要时 `docker compose --profile s3 up -d`。默认只发布到 `127.0.0.1`；局域网加 `CHAT_PUBLISH=0.0.0.0`。模板含开发凭据，没有公网 TLS、TURN 或生产安装器。完整生产自托管见 [SELF_HOSTING.md](SELF_HOSTING.md)。

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
| 2 | reaction、系统通知、presence、时间线性能验收。删除、回复、typing、作者编辑、`@username` / `@everyone`、未读/草稿已落地 |
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

### 侧栏与频道管理（2026-09-21）

频道/账号列为 260 px，给底部头像、用户名和麦克风/耳机控件留出间距。已实现紧凑频道分组、2 px 选中行圆角和分组旁的新增按钮。所有者可添加文字/语音频道，右键打开、重命名、添加同类频道或删除。添加与重命名直接在侧栏原位输入，Enter 保存、Esc 取消，无遮罩和居中弹窗；删除在原行展开确认，失败就地显示错误，创建语音频道不会自动加入通话。管理请求走 HTTP，变更通过 Gateway 与 SQLite 同步。

验证：最终客户端 Release 构建（0 警告/错误）、本地 API / Gateway 聚焦检查、49 项客户端契约检查通过；macOS 真实窗口验证行内添加、右键原位编辑、Enter 保存和 Esc 取消。删除 API 与确认入口已验证；远端部署、PostgreSQL 实际删除和三平台窗口尚未验证。
