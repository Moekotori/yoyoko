# Development

## 环境

- .NET SDK 10，global.json 允许同一 major 最新 feature band；依赖固定于 Directory.Packages.props 与 packages.lock.json。Windows 的 `start.cmd` 若找不到 SDK 10，会用官方脚本安装到 `%USERPROFILE%\.dotnet`。
- Rust stable ≥ 1.90（本机验证 1.98.1），Cargo.lock 固定依赖。
- CMake ≥ 3.24 + C++20 编译器。
- Docker Compose v2：日常跑完整服务端用 `docker compose up -d --build` 或 `npm run up`。没有 Docker 时仍可用 `cargo run -p chat-server`（`local:` 文件库）。
- Node.js ≥ 22 用于协议 fixtures 与对本机 API/Gateway 的 live 检查；无 npm 依赖。

所有命令从仓库根目录执行。

## 构建与运行

```sh
docker compose up -d --build
# 或 npm run up
# 局域网：npm run up -- --lan

dotnet restore Chat.sln --locked-mode
dotnet build Chat.sln -c Release --no-restore
dotnet run --project src/client/App -c Release --no-build
cargo build --locked -p chat-media-worker
```

改 Rust 服务端、不经过容器时：`cargo run --locked -p chat-server`。

`chat-media-worker` 按需启动：枚举设备，并作为 LiveKit 客户端发布/订阅麦克风。未构建该二进制时，设置里只有“系统默认”，进语音会明确失败。Compose 已含 LiveKit。只想本机跑媒体、不用整套堆栈时：

```sh
docker compose up -d redis livekit
cargo build --locked -p chat-media-worker
```

Windows 无 Docker 时，用官方 `livekit-server` 单节点（不强制 Redis；聊天 Backend 当前也不连 Redis）。将 `livekit_1.9.0_windows_amd64.zip` 解出到 `.tools/livekit-server.exe`（该目录已 gitignore），然后：

```sh
.tools\livekit-server.exe --config deploy/livekit.host.yaml
```

`start.cmd` 会在 7880 空闲时自动拉起该进程；检测到局域网地址时用 `--node-ip` 宣告该网卡（`deploy/livekit.host.yaml` 已绑 `0.0.0.0`）。LiveKit 1.9 的 YAML 没有顶层 `node_ip`。有 Docker 时执行上面的 Compose 命令。`config.toml` 的 `[rtc]` 必须与 `deploy/livekit.yaml` / `deploy/livekit.host.yaml` 的 `keys` 一致（默认 `devkey` / `local-development-only-change-me-32`）。`rtc.public_url` 可保持 `http://localhost:7880`：发现与 `voice/join` 若看到局域网 `Host`，会把 loopback RTC 改写成该网卡 IP 并保留 7880 端口。本机无 LiveKit 时 join 会报连接失败，频道成员列表仍可用。音频进程退出或 LiveKit 断开后，已在频道内会自动重签 token 并重连，最多 5 次。

网页语音：浏览器打开 `{origin}/voice/{channel_id}`（桌面语音频道右键复制）。页面只显示该房间，没有文字频道。访客令牌不能列频道或读消息。本机可用 `http://localhost:8080/voice/...`；其它设备用局域网地址，LiveKit 需监听 `0.0.0.0`。`npm run lan` 会设置 `CHAT__RTC__PUBLIC_URL`。

客户端默认连接配置的服务器，并恢复或首次注册账号；可在「设置 → 服务器」输入 `http://localhost:8080` 并连接。账号资料在设置修改，首屏不再要求账号密码；不会自动加入语音。开发工程设置 `UseAppHost=false`，由已安装的 dotnet runtime 直接运行；独立安装包的 host/signing 属于后续发布工作。

局域网测试（同一 Wi-Fi 下的另一台电脑或本机第二客户端）：

```sh
npm run up -- --lan
# 无 Docker 时仍可用 npm run dev，会回退到 cargo 并绑 0.0.0.0
```

客户端输入打印出的 `http://192.168.x.x:8080`。默认 Compose 与 `cargo run` 只发布/监听回环。`CHAT_PUBLISH=0.0.0.0` 覆盖 Compose 发布地址；无 Docker 时 `CHAT_LAN_PORT` / `CHAT_LAN_HOST` 覆盖 cargo 局域网模式。

```sh
cargo run --locked -p chat-server -- check-config
# 仅在 PostgreSQL 可达时执行显式迁移：
cargo run --locked -p chat-server -- migrate
cmake -S native-media-core -B build/native -DCMAKE_BUILD_TYPE=Release
cmake --build build/native --config Release
```

## 开发热重载

Windows 双击根目录 `dev.cmd`，macOS 双击 `dev.command`，或运行：

```sh
dev.cmd
./dev.command
# 等价入口
start.cmd --watch
./start.command --watch
```

脚本以 Debug 配置运行客户端并连接配置的局域网服务器，不管理服务端进程。普通 `start.cmd` / `start.command` 仍使用 Release。两种模式共用客户端缓存，切换前先关闭原客户端。

普通启动优先使用「设置 → 服务器」上次连接成功的地址；没有保存选择时读取 `default_instance_url`（当前 `http://localhost:8080`，可用 `CHAT_DEFAULT_INSTANCE_URL` 覆盖）。Windows 的 `start.cmd` 会先拉起本机服务端再打开客户端。Core 的 `WorkspaceConnection` 统一负责启动和设置连接：优先恢复实例账号，只有首次没有账号记录才通过标准注册 API 创建随机独立账号，不保存随机注册密码。已有账号的会话过期、网络失败或凭据丢失不会触发新账号注册。「设置 → 资料」只编辑当前账号的头像、横幅、显示名和用户名，不再提供登录/注册表单。内网实例尚无社区时创建一个真实社区及 `general`。账号、会话与缓存仍按实例隔离，同源发现校验保持不变；服务器不可达时可重试或打开设置，不回退到其他服务。

`localhost` / `http://localhost` 是客户端的默认服务器快捷地址，由 Core `WorkspaceAddress` 解析到 `default_instance_url`（包含端口，支持环境变量覆盖）。设置连接和原添加实例入口使用同一连接流程；成功后保存实际地址，继续执行同源发现与账号恢复。带端口地址、其他域名与显式 HTTPS 不重定向；不会修改系统 hosts 或启动代理。

设置中的服务器选择优先于配置和环境默认值。需要隔离本机测试时使用独立 `CHAT_CACHE_DIRECTORY`；没有保存选择且默认 URL 为空时，仅显示连接/设置入口。`--local-workspace` 保留为 localhost 默认值兼容参数。刷新凭据仍沿用项目现有 0600 开发文件存储，系统凭据库适配尚未实现；本次不改变服务端协议。

- App 和 UI 接入免费开源的 [HotAvalonia](https://github.com/Kira-NT/HotAvalonia)，版本统一锁定。保存现有 `.axaml` 布局、样式时直接重载，不必手动构建。终端显示重载文件和解析错误；修正 XAML 后重新保存即可。
- C# 由 [dotnet watch](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch) 处理；不能热重载的结构性变更自动重启。修改构造函数或初始化代码不会重新执行已完成的初始化，必要时停止并重新运行脚本。
- 增加/删除控件文件、修改项目配置或嵌入图片后，可能需要重新启动开发模式。未引入图片资源注入依赖，不承诺图片文件原位热替换。
- XAML 重载可能重置控件内部状态，自动重启也不保留内存中的输入和登录状态。只关闭客户端窗口不会结束监听；在启动终端按 Ctrl+C 结束客户端监听，服务器继续运行。
- 热重载限于客户端。Rust 服务代码改动仍需重新构建并启动服务端。
- HotAvalonia 及运行时 XAML Loader 在 Release 中移除；开发模式的编译器和文件监听开销不能用于评价 Release 的内存目标。仅在 Debug 输出 HotAvalonia 诊断日志。

## 配置

服务端唯一应用配置为 `config.toml`；`CHAT_CONFIG=/path/config.toml` 指定路径。所有字段支持 `CHAT__SECTION__KEY` 覆盖，例如 `CHAT__SERVER__NAME`、`CHAT__DATABASE__MAX_CONNECTIONS`、`CHAT__STORAGE__MAX_BYTES`；数组用逗号分隔，例如 `CHAT__SERVER__ALLOWED_ORIGINS=https://a.example,https://b.example`。附件上限默认 24 MiB，允许 64 KiB..=256 MiB。不要把真实密钥写入版本库。`check-config` 仅显示校验结果，不打印秘密。

客户端配置为 App/appsettings.json，支持 `CHAT_PRODUCT_NAME` / `CHAT_CACHE_DIRECTORY` / `CHAT_DEFAULT_INSTANCE_URL`。默认缓存为 .NET LocalApplicationData 下 `chat-desktop/cache.db`。界面语言、紧凑布局、自定义背景和键盘快捷键等写入同目录 `preferences.json`；背景文件复制到同目录 `wallpaper/`。静图按最多 1920×1080 解码，视频按最多 1280×720、20fps 解码（磁盘上限 2 GiB），不把片源装进内存。视频优先 `ffmpeg`（`CHAT_FFMPEG` 或 PATH），否则尝试系统解码器。深色/浅色写入 `color_scheme` 并立即切换主题。首次启动跟随系统（`zh*` → 简体中文，`ja*` → 日语，其余为英文），设置中可改为中文、英文或日语。`CHAT_LOCALE`（`zh-Hans` / `en` / `ja`）仅在尚未保存过语言时生效。凭据库尚未接入，SQLite 不存令牌。

新增界面文案：在 `src/client/Localization/TextCatalog.cs` 的 `Rows` 加一行 `(Key, English, 中文, 日本語)`，并在 `TextKey.cs` 加同名常量。XAML 用 `{i18n:T Key}`，C# 用 `_text.Get(TextKey.Key)` 或 `I18n.T(TextKey.Key)`。foundation check 会核对三语和 TextKey 是否对齐。用户语言包格式见 [docs/i18n/README.md](docs/i18n/README.md)；设置里可导出完整模板并拖入 `.json` 导入。

只改产品名不需要改代码包名；`/.well-known/lightchat` 是固定协议路径，产品重命名不更改协议标识。

## 有限验证

```sh
npm test
npm run test:live
dotnet run --project tests/client -c Release
cargo test --workspace --locked
ctest --test-dir build/native -C Release --output-on-failure
python3 scripts/check_boundaries.py
```

`npm test` 只读共享 JSON fixtures，确认 `seq` 在 JS 中保持十进制字符串。`npm run test:live` 启动 `chat-server`（已有 `target/debug/chat-server` 则直接用，否则 `cargo build --locked`），默认 `127.0.0.1:18080`，配置为 `tests/node/config.toml`，检查 health、discovery、业务路由关闭和 Gateway 握手。已有服务时用 `CHAT_BASE_URL=http://127.0.0.1:8080 npm run test:live`。

client checks 是无额外框架依赖的可执行 contract suite：共享 JSON、64 位序列、URL 边界、权限、缓存账号/实例隔离、分页与持久化。Rust 检查共享 fixture、权限、路由；C consumer 检查 ABI 生命周期与显式未实现状态。无需为外壳布局写大量镜像实现的测试。

格式/lint：

```sh
dotnet format whitespace Chat.sln --no-restore
cargo fmt --all
cargo clippy --workspace --all-targets --locked -- -D warnings
```

## CI

本地入口与 GitHub Actions 共用 `scripts/ci.mjs`：

```sh
npm test                 # 协议 fixtures
npm run test:live        # 启动本机 API 后测 HTTP / Gateway
npm run dev              # 0.0.0.0 监听，供本机/局域网客户端连接（lan 同义）
npm run ci               # fixtures + live（日常本地管线）
npm run ci -- --full     # 再加 .NET、CMake、边界检查和 Rust
npm run ci -- protocol   # 只跑指定 job：protocol|live|desktop|native|structure|server
```

GitHub Actions（`.github/workflows/ci.yml`）以节省额度为默认：

- `main` push / PR：只运行一个 Linux `quick` job，检查 Node fixtures、模块边界和 Compose 配置；纯 Markdown、UI/ADR/i18n 文档改动不触发。协议 fixtures 仍触发。
- 手动 `Run workflow` 选择 `scope=linux`（默认）：quick 通过后，运行 Linux .NET Release / client contracts / CMake、C# 格式检查和 Rust 服务端检查。
- `scope=full`：额外运行 Windows/macOS 桌面构建；上述检查通过后才构建并启动 Compose、运行 Node live 检查并清理容器。
- `scope=quick`：手动只跑轻量检查。同一分支的新运行会取消旧运行。
- 服务端 Rust fmt/clippy/test 仅选择 `chat-server`、`chat-domain`、`chat-protocol`，不再误编译桌面媒体 worker；PostgreSQL migration 连续运行两次验证幂等，live 复用 debug 构建，避免日常重复编译 release。
- 容器构建保留完整锁文件，仅携带媒体 worker 的 workspace manifest 与空 target 路径以供 Cargo 解析；不会编译或打包媒体 worker。服务端独立仓库导出仍需收敛 workspace 元数据。
- 自动 quick 不代表编译或跨平台通过；媒体 worker 编译/设备验收需单独按媒体开发说明执行。

```sh
gh workflow run ci.yml -f scope=linux
gh workflow run ci.yml -f scope=full
```

远程平台结果须以实际 workflow 执行为准。三平台编译不等于三平台 GUI、分发签名或音频硬件验收。

## 性能

见 [benchmarks/README.md](benchmarks/README.md)。先测 Release 实际进程和窗口，禁止用 Debug 编译器/构建进程内存代替客户端内存。后续引入 worker 时必须合计整棵进程树。

服务端热路径走进程内 `HotCache`（短 TTL、有上限），合并鉴权 SQL、READY 一次拉频道、`@` 一次拉成员用户名。消息正文仍只在 Store（Postgres / `local:`）里，不进 Redis、不做无界缓存。限流表超过 2048 个 key 时淘汰过期项。
