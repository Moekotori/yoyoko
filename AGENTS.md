# 开发约定

本文件约束本仓库后续开发，以用户当前明确要求为准。本项目是独立聊天软件，不继承父目录 ECHO 项目的曲库、播放或歌词业务需求。

## 产品方向与阶段

- 构建类似 Discord 核心体验、但更轻量、低内存、启动快、响应快的聊天软件，目标平台为 Windows、macOS、Linux。
- 用户能够自托管完整服务端；官方实例与自托管实例使用同一套协议，基本聊天、社区和自托管能力不设计成官方专属功能。
- 产品名为 yoyoko；产品名集中于配置，不写死进业务模块、命名空间或存储路径。代码使用 `Chat` / `chat`，固定协议发现路径不随品牌改名。
- 当前基线为 Phase 1：两个真实客户端文字聊天（含发图）。按 [ROADMAP.md](ROADMAP.md) 分阶段开发，不在当前阶段扩写完整 Discord clone。用户明确要求推进后，直接按授权阶段执行并同步更新文档，不为例行实现反复索取确认。
- 近期目标是两个客户端真实聊天；完整聊天体验、角色管理、语音、共享和生产自托管逐步推进。
- 不提前实现 Federation、全球统一账号、完整 E2EE、任意进程内插件或自研 SFU。未来移动端和 Web 复用协议，当前不开发其 UI。
- 未实现能力必须明确标记 `Not implemented yet`。禁止用假消息、假在线状态、假媒体成功或大量空类冒充完成。

## 固定技术方向

| 层级 | 技术与职责 |
| --- | --- |
| 桌面 | Avalonia + C#/.NET，轻量 MVVM、编译绑定 |
| 业务核心 | C#，独立于 UI、数据库和具体网络实现 |
| 媒体核心 | C++，稳定且有版本的 C ABI |
| 服务端 | Rust + Tokio + Axum，模块化单体 |
| 持久数据 | PostgreSQL 为服务端事实源，SQLite 为客户端缓存 |
| 临时状态 | Redis；typing 等短期事件不入业务数据库 |
| 附件 | S3 Compatible Storage，自托管优先 MinIO |
| RTC | WebRTC + LiveKit SFU，媒体不经过聊天 Backend |

任何设计都要考虑 Memory、CPU、Latency、Maintainability、Self-hostability、Cross-platform。不能以“以后再优化”为由接受明显无界的分配、队列或全量加载。不为尚未出现的规模引入微服务。日后分离服务端指把服务端整包抽成独立产品，不是把单体拆成一堆业务进程。

## 模块与文件必须拆清楚

- 用独立项目/crate、公开契约和依赖方向落实模块化，不能只建文件夹、所有代码仍互相访问。
- 当前客户端项目为 `App / Domain / Protocol / Localization / Core / Networking / Storage / Media / UI`。服务端划分 `domain / protocol / app`；app 内按业务能力分目录（auth、community、channel、message、attachment、voice 等），各自持有 handler、service、repository port 和 adapter，禁止继续堆进单个 `api.rs` / `services.rs` / `store.rs` / `postgres.rs`。
- 界面语言是独立 Localization 模块：公开 Locale、ILocalePreference、ITextCatalog；Core/Networking 抛 `ClientFault` 键，不查文案表。语言是客户端级偏好，不是实例身份。
- App 只负责组合、配置与生命周期；Domain 不依赖 UI/网络/存储；Core 定义业务与所需端口；基础设施实现端口；UI 通过 Core 操作业务。
- Window/code-behind 只放视图装配与必要交互适配。禁止把业务、SQL、HTTP、媒体状态持续堆入 MainWindow、单个 ViewModel 或万能 Service。
- 一个文件只承担一类职责：视图、ViewModel、HTTP 路由、业务编排、持久化、DTO 映射、配置、迁移分开维护。文件随职责增长必须拆开，禁止把新能力继续追加进已有万能文件。
- 相关的小型类型可以同文件，不为每个类型机械地创建项目，也不为未来可能用到的功能建空模块。
- 每个模块拥有自己的状态、数据访问和释放方式。默认隐藏实现，仅公开必要 API；禁止循环依赖、访问内部实现和绕过模块接口写表。
- 连接、后台任务、取消令牌、事件订阅、图片、native handle 必须有明确的所有者与释放路径。
- 保持 `scripts/check_boundaries.py` 与架构一致；新增允许引用必须有职责依据，不能通过放宽检查掩盖依赖倒置。服务端同样：`chat-domain` 不依赖 Axum/SQLx；`chat-protocol` 只含 DTO 与版本常量；`chat-server` 的 handler 不直接写库。
- 不默认引入反射扫描 DI、全局消息总线、层层 CQRS/Mediator 或无职责边界的 Common/Utils。

## 耦合与服务端分离

当前为便于协议共进化，客户端与服务端同仓开发，这是过渡形态。服务端必须始终能整包抽出为独立产品（独立仓库、独立构建/测试/镜像、独立发布），不依赖桌面客户端的源码、工程或进程。抽出条件是协议稳定或显式版本、独立发布节奏、镜像部署；未到条件前仍同仓，但不得增加会挡住抽出的耦合。

唯一允许的跨端耦合：

- 协议文档、`docs/protocol/fixtures`、`protocol_version` / `api_version`
- 两端各自维护的 Protocol DTO（C# `Chat.Protocol` 与 Rust `chat-protocol`）；改字段必须同时审查两端与 fixtures
- 运行时只走 HTTP `/api/v1` 与 Gateway WebSocket；客户端通过 `/.well-known/lightchat` 发现任意兼容实例

禁止的耦合：

- 客户端引用服务端 crate/源码，或服务端引用客户端项目、Avalonia、SQLite 缓存、桌面凭据路径
- 共享领域对象、共享进程、把聊天 Backend 嵌进桌面进程，或绕过协议走内部函数/文件/数据库捷径
- UI 直接打 HTTP/SQL；handler 直接访问 SQLx/本地文件；Service 泄露基础设施类型
- 一份配置、一份路径或一个类型同时服务桌面与 Backend 实现细节
- 为“方便本地开发”把客户端目录写进服务端，或让服务端假设本机一定有桌面 UI

分离时拆的是「客户端仓库 vs 服务端仓库」，不是拆业务微服务。一个 API/Gateway 进程仍然是服务端形态；PostgreSQL、Redis、MinIO、LiveKit 是基础设施，不因此变成业务服务。新代码必须在抽出 `src/server` 后仍能独立成立。

## UI 从简

- 通过清晰的信息层级、对齐、间距和文字组织界面；少装饰、少边框、少圆角，默认平面布局，普通控件圆角控制在 0–2 px。
- 不滥用圆角卡片、胶囊、阴影、渐变和嵌套描边。仅在确实需要区分区域或表达交互状态时使用边界。
- 不擅自增加小字说明、脚注、提示条或无功能装饰。必要的错误、连接状态、权限说明要简短准确。
- 保留实例栏、社区/频道导航和主要内容的职责区分，不在架构阶段实现复杂 UI 或虚假聊天演示。
- 保证键盘操作、焦点和可读对比度；视觉调整用真实窗口检查，编译通过不等于界面验收。
- 页面/频道切换需要动效时使用短促、可中断的轻量过渡；不添加闲置循环动画，隐藏/最小化时停止无用绘制。

## Multi-Instance 与身份隔离

- 禁止以全局 `CurrentUser / CurrentServer` 表示整个应用身份。使用 `InstanceManager` 管理独立 `InstanceContext`。
- 每个实例独立持有 InstanceId、BaseUrl、账号、会话凭据、Gateway、社区/频道状态和缓存作用域。
- 远端实体以 InstanceId + EntityId 标识，私有缓存再加 AccountId。不同实例的同名账号、同 ID 实体不能混用。
- 实例之间不通信；一个客户端同时连接多个实例不等于 Federation。
- 退出账号、实例断开或权限撤销时，按对应作用域取消任务、释放连接、处理凭据和私有缓存，不影响其他实例。
- Access token 只在会话内存中持有；refresh token 通过系统凭据库接口存储，不进入 SQLite、配置、URL、日志或 Git。
- 用户输入域名后通过 `/.well-known/lightchat` 发现服务。默认 HTTPS/WSS，本机开发可允许回环 HTTP/WS；不得静默把认证数据发给不可信发现地址。

## 消息、同步与权限

- 服务端是事实源，客户端遵循 `Remote → Sync → SQLite → UI`；启动先显示缓存，后台连接和增量同步，不能等网络完成才显示首屏。
- 公开 ID 使用 UUIDv7 或同等级可排序分布式 ID，不使用数据库自增 ID。
- 消息模型从一开始容纳 type、reply、edit/delete、mention、attachment/image/file、embed、reaction、system 和未来 encrypted payload，而不是只有 text 字段。
- HTTP 负责注册、登录、历史、上传、社区/频道管理、搜索和管理操作；Gateway 负责实时事件，不把所有业务 RPC 塞进 WebSocket。
- 协议独立、有 `protocol_version` / `api_version` 与 `/api/v1`。修改字段时同时审查 C# 与 Rust DTO、共享 fixtures 和协议文档；不能不声明版本变更就破坏旧客户端。
- Gateway 包含 sequence、heartbeat、断线恢复与有界 replay 设计。只有 SQLite 事务已提交的事件才能推进恢复游标；处理重复、缺口、session 过期和取消，不能掉线后默认全量下载。
- 权限采用 Role + u64 Permission + Channel Override，按 base role、server roles、channel overrides 计算；具体聚合优先级见架构文档。
- 所有鉴权和权限验证必须在服务端完成，UI 隐藏按钮不构成授权。基础成员与读写权限随第一批业务 API 实现，不能等完整角色管理阶段才补。
- Service 不泄露 SQLx/HTTP 类型，Repository adapter 不承担 UI 或授权入口职责。认证尚未实现时 fail closed。
- PostgreSQL 先用全文检索，不提前引入 Elasticsearch。Presence、typing、voice state 的高频变化不持续写 PostgreSQL。

## 内存与性能约束

- 无语音/共享时闲置内存尽量向 50–100 MB 靠拢，这是架构目标，不是未经实测的承诺。不能接受闲置 300–500 MB 后以“以后优化”搪塞。
- 消息时间线必须虚拟化、增量加载、复用和分页；只保留可见附近几十至几百条模型，不一次加载整个频道。当前预算为最多 300 条、默认页 50 条、单页上限 100 条。
- 数据层按页缓存，磁盘和内存都要有预算与淘汰策略。常量或文档中的预算不等于对应淘汰机制已经实现。
- 图片优先 128/256/512 缩略图，原图按需加载。不可见 GIF 暂停、视频不解码、资源及时释放。文件流式传输，不把整个文件装进 MemoryStream。
- 网络帧、队列、批次、并发、预取和重试必须有上限及取消机制；不使用无界 Task.WhenAll 或无限积压事件队列。
- Release 日志默认不过量输出 Debug，使用有界异步队列和滚动上限，不记录私密消息与令牌。
- 性能记录注明构建模式、系统、窗口状态、负载和进程范围。媒体进程加入后统计整棵进程树；不要混用 RSS、private working set 和托管 heap。
- 随功能落地记录 idle RAM/CPU、100/1000/10000 条消息负载、渲染/切频道延迟、启动/重连时间、语音与共享 CPU。功能未实现时写明未测，不制造数据或先搭庞大仪表盘。

## Native Media 边界

- 音频、WebRTC、Opus、AEC、降噪、VAD、捕获和编解码保持在 Native Core；C ABI 不暴露 C++ 类、STL 对象或异常。
- C# 只传控制命令与低频状态，不逐帧传 PCM / video byte[]。零拷贝优先：Capture → GPU handle → hardware encoder。
- Windows 使用 WGC/D3D11，macOS 使用 ScreenCaptureKit/IOSurface，Linux 使用 xdg-desktop-portal/PipeWire/DMA-BUF；平台实现分开。
- 首期音频方向为 48 kHz mono Opus、DTX/FEC、AEC、RNNoise、AGC/VAD；视频优先 H.264 与 1080p30/60。未来 AV1/更高分辨率不能挤占当前阶段。
- 实时帧队列保持 2–3 帧，满时丢旧帧；音频实时回调禁止分配、阻塞锁、文件/网络 I/O、RPC 或日志。
- Idle 不加载媒体库、不访问设备、不启动 worker；离开媒体会话后释放资源。
- `noexcept` 或捕获异常不等于 native crash 隔离。当前库仅作 ABI 骨架；真实媒体接入前使用按需 worker，保证崩溃不会终止主客户端。Phase 7 再完善进程恢复、回收与分发，不推迟基本隔离要求。

## 自托管、配置与安全

- 自托管目标是 `docker compose up -d`，包含 API/Gateway、PostgreSQL、Redis、MinIO、LiveKit；当前 Compose 基础和最终生产安装能力要明确区分。
- 应用配置集中于 `config.toml`，所有字段支持环境变量覆盖；服务端迁移必须有版本，不能在正常启动时隐式破坏数据库。
- 官方和自托管协议一致，不给官方服务开特殊业务接口或权限后门；单实例账号不依赖官方身份服务。
- 认证使用 Argon2id、会话/access token、refresh rotation，并按场景落实 rate limiting、CORS、输入校验和 CSRF 防护。
- 附件校验大小、MIME 与扩展名；存储 key 为随机 UUID，用户文件名只能作为元数据，不能作为物理路径。
- 将样例开发凭据与真实秘密区分开。SSH 前先告知用户连接目标和用途；不把用户提供的 SSH 密码、令牌或生产凭据写入仓库文件、日志或提交记录。
- Web Admin、Bot/Webhook、E2EE 等依路线图推进。Bot 使用独立 token，后续插件不直接加载进入 Server Process。

## 工作方式与验证

- 开工先检查 Git 状态和适用约定，保留用户及其他任务的修改；不 `git add -A`、reset/clean、强推或隐式 rebase，不擅自提交/发布。
- 按已授权范围推进。较大变更先说明具体模块和范围；授权已经清楚时不再为常规可逆操作反复确认。
- **不要过度测试，测试太深浪费时间。** 只跑相关构建、必要聚焦检查和一次适当的真实运行验证。纯文档修改检查内容、路径和格式即可，不重跑整套工程。
- 测试通过且没有新变更、失败或疑点后停止扩大验证。避免写大量仅复述实现的测试、重复压测或长时间轮询。
- 区分静态检查、编译、聚焦测试、真实窗口、硬件、三平台运行和生产验收；不能把其中一种写成另一种已经通过。
- 更新功能时同步 README、阶段状态、协议/ADR 和必要开发说明，明确已实现、仅骨架、未验证、下一阶段；不要把性能目标写成既成结果。
