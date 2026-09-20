# Architecture

## 范围与状态

本仓库只完成 Phase 0。建立可编译边界、可运行的最小外壳和必要契约，避免先写完整聊天再重构。

| 能力 | 当前状态 |
| --- | --- |
| Avalonia 桌面、简洁三栏布局、编译绑定 | 已实现 |
| 实例发现、添加、SQLite 保存、离线恢复 | 已实现；最多 32 实例 |
| 多实例上下文、独立账号/连接所有权 | 已实现抽象；尚无认证会话 |
| HTTP / WebSocket 有界传输 | 已实现；重连编排待 Phase 1 |
| 消息分页缓存、账号/实例隔离、注销清理端口 | 已实现并有聚焦检查 |
| Rust 健康检查、发现、版本握手 | 已实现；无业务 API |
| Domain / Service / Repository / PostgreSQL 适配边界 | 已实现；权限服务默认拒绝 |
| PostgreSQL 初始 migration | 已编写；运行状态见验证记录 |
| Native C ABI 与句柄生命周期 | 已实现；能力位为 0 |
| 账号、聊天、同步、语音、屏幕共享 | **Not implemented yet** |
| Redis Presence、S3 上传、LiveKit token | **Not implemented yet**；配置和部署基础已准备 |

## Monorepo

```text
Chat.sln / Directory.*.props / global.json
Cargo.toml / Cargo.lock
src/
  client/
    App/             组合根、配置、窗口与应用生命周期
    Domain/          Instance / Account / Server / Channel / Message / Role / Permission
    Protocol/        JSON DTO、版本、source-generated serializer
    Core/            Instances、Messaging、Realtime 业务端口与预算
    Networking/      HTTP discovery、WebSocket transport
    Storage/         SQLite 实现、版本化迁移、分页缓存
    Media/           媒体契约、不可用实现、Native ABI 声明
    UI/
      Shell/         窗口与轻量 ViewModel
      Instances/     实例栏与添加实例视图
      Channels/      频道栏
      Components/    轻量命令与属性通知
      Styles/        共享颜色、间距和控件规则
  server/
    domain/          独立 Rust 领域 crate
    protocol/        独立协议 crate
    app/src/
      api.rs         HTTP 路由与 JSON 映射
      gateway.rs     有界 WebSocket 握手
      channel/       service + repository port
      database/      PostgreSQL adapter + migration runner
      configuration.rs
native-media-core/
  ffi/               C ABI 与 C++ 生命周期
  audio/ video/ capture/ codec/ rtc/ platform/  明确标记未实现
  tests/             C 语言 ABI consumer
protocol fixtures → docs/protocol/fixtures/
database/migrations/ PostgreSQL schema
compose.yaml / deploy/ Docker 基础环境
.github/workflows/   三平台编译、Node 协议检查、格式、边界与服务端检查
scripts/             本地 CI 入口、边界检查与进程采样
tests/node/          共享 fixture 的 JS 解码与 live HTTP/Gateway 检查
benchmarks/          性能测量方法与当前结果
```

## Client

依赖方向：

```text
App (composition)
 ├─ UI → Core → Domain + Protocol
 ├─ Networking → Core + Protocol
 ├─ Storage → Core + Domain + Protocol
 └─ Media (control contract only)
```

UI 不引用 SQLite、HTTP 或原生库；Domain 不依赖 Avalonia。每个模块是独立 csproj，新增引用必须通过边界检查。暂不引入反射 DI、全局消息总线、CQRS 或为每个类建一个项目。

应用先显示窗口，再在工作线程初始化 SQLite、读取本地实例。业务扩展必须走 `Remote → sync transaction → SQLite → UI`。目前发现配置先持久化后更新 UI；消息缓存有实现，但远程同步尚未接线。

无假频道、假消息或假在线状态。频道栏为空是因为登录尚未实现。UI 以平面、留白和文字层级表达结构，仅保留必要分隔线和 2 px 控件圆角。未来消息视图使用内置虚拟化 ListBox / VirtualizingStackPanel，不外套无限高度 ScrollViewer。

## Multi-instance

`InstanceManager` 管理 `InstanceId → InstanceContext`；每个 context 拥有 descriptor、account 和 gateway 生命周期。`EntityKey` 包含 InstanceId 与实体 ID。账号退出时必须释放该实例连接、撤销 token、清除该账号私有缓存；其他实例不受影响。

实例提供稳定 UUIDv7 身份；客户端不会用域名替代身份，也不把不同实例的同名账号视为同一人。同一实例从不同地址添加时拒绝隐式迁移。域名迁移需要后续显式流程。每实例第一阶段只登录一个账号，存储仍按账号隔离。

Access token 仅会话内存持有；refresh token 通过 `ICredentialVault` 接入 Keychain / Windows Credential Manager / Linux Secret Service。适配器 **Not implemented yet**；不把凭据塞进 SQLite、发现响应或 WebSocket URL。

默认 HTTPS，仅回环地址允许 HTTP。发现响应最多 16 KiB，禁用隐式 HTTP 跳转；API 与 Gateway 必须同源，阻止把认证数据发送给任意发现端点。CDN / RTC 可有独立安全源。

## Server

Rust + Tokio + Axum 模块化单体。一个 API/Gateway 进程；PostgreSQL、Redis、S3 和 LiveKit 是基础设施，不拆业务微服务。

`api → service → repository port ← PostgreSQL adapter`。领域对象无 SQLx / Axum 类型。ChannelService 展示实际边界：先检查授权，再读 repository。权限实现当前 fail closed。尚无业务路由，不会把无鉴权的数据库读写暴露到网络。

Phase 1 将按 Auth、Server、Channel、Message、Permission 分模块，保留 service 端口；不提前创建无实现的万能服务。所有权限在服务端检查，客户端 PermissionResolver 只用于 UI。权限顺序：base → server roles → everyone channel override → aggregated role overrides → member override；administrator bypass。Postgres NUMERIC 保存 u64，协议用十进制字符串。

服务端异步日志使用最多 1024 行有损队列，默认 Info。容器 stdout 按 10 MiB × 3 滚动；裸进程由宿主 journald / 日志收集器设置等价限制。永不记录 token / password / 完整私密消息。Gateway 包最大 64 KiB、握手 5 秒超时；业务认证、rate limiting、membership、refresh rotation 是 Phase 1 上线前置条件。

## Native media

C++20 独立 CMake 工程；只导出版本化 C ABI，opaque context 明确所有权，错误码不假成功。当前 capabilities=0。音频、视频、捕获、编解码、RTC 与平台目录仅说明未来职责。

客户端 idle 不加载 native 库、不创建设备、不启动媒体进程。未来在启用真实媒体前接入按需 worker：control IPC（版本、request ID、session generation、取消与故障状态）进入 worker，原始 PCM / GPU frame 不进 C#。这是为了满足“不允许 Native crash 拖死 UI”的约束；仅 catch C++ 异常不能隔离 segfault。

Capture → GPU-native handle → encoder；Windows D3D11、macOS IOSurface、Linux DMA-BUF。队列 2–3 帧，丢弃旧帧。LiveKit SFU 负责 WebRTC，不经聊天 Backend。真正实现设备、RNNoise、AEC、H.264、GPU zero-copy、跨平台权限与进程恢复仍在后续阶段。

## 预算与扩展边界

| 资源 | 预算/策略 | 当前落实程度 |
| --- | --- | --- |
| Idle RSS | 50–100 MiB 方向性目标 | 必须实测，不是已保证的指标 |
| 时间线模型 | 最多 300 条 | Core 常量；消息 UI 尚未实现 |
| 单页 | 默认 50，上限 100 | SQLite 参数校验和 keyset pagination |
| 消息磁盘缓存 | 每账号/频道最近 1000 条 | 写入时事务裁剪 |
| 缓存总磁盘 | 256 MiB 目标 | 全局字节淘汰 **Not implemented yet** |
| SQLite 页面缓存 | 单连接 2 MiB、短连接、WAL | 已实现，无常驻连接池 |
| 图片内存 | 16 MiB 目标，128/256/512 缩略图优先 | 图片 pipeline **Not implemented yet** |
| 网络 | 有界收包、背压消费、串行发送 | 已实现传输层，无后台轮询 |
| 视频 | 2–3 帧 | 尚无媒体分配 |

不对 10,000 条消息建立常驻 ViewModel；可见资源销毁、GIF 不可见暂停、视频不解码会在 Phase 2/5 的实际实现中验证。Native AOT 是后续发布选项，当前不宣称 AOT / 裁剪发布已验证。协议 JSON 使用源生成序列化，不默认动态插件。Bot、webhook、E2EE envelope 保留扩展点，Federation 和任意进程内插件不实现。

参考：[Avalonia 编译绑定](https://docs.avaloniaui.net/docs/data-binding/compiled-bindings)、[虚拟化与性能](https://docs.avaloniaui.net/docs/app-development/performance)、[Axum WebSocket](https://docs.rs/axum/latest/axum/extract/ws/)。
