# Development

## 环境

- .NET SDK 10，global.json 允许同一 major 最新 feature band；依赖固定于 Directory.Packages.props 与 packages.lock.json。
- Rust stable ≥ 1.90（本机验证 1.98.1），Cargo.lock 固定依赖。
- CMake ≥ 3.24 + C++20 编译器。
- Docker Compose v2 仅部署环境需要；桌面和 HTTP discovery 无需 Docker 即可启动。
- Node.js ≥ 22 用于协议 fixtures 与对本机 API/Gateway 的 live 检查；无 npm 依赖。

所有命令从仓库根目录执行。

## 构建与运行

```sh
dotnet restore Chat.sln --locked-mode
dotnet build Chat.sln -c Release --no-restore
dotnet run --project src/client/App -c Release --no-build
cargo run --locked -p chat-server
```

客户端输入 `http://localhost:8080` 并点击“添加实例”。重新打开后可以离线看到已保存实例。发现阶段不会自动登录、开启 Gateway 或访问麦克风。开发工程设置 `UseAppHost=false`，由已安装的 dotnet runtime 直接运行；独立安装包的 host/signing 属于后续发布工作。

局域网测试（同一 Wi-Fi 下的另一台电脑或本机第二客户端）不要用默认的 loopback 监听。使用：

```sh
npm run dev
```

服务会绑到 `0.0.0.0:8080`，并打印本机局域网地址。在客户端输入该地址（可省略 `http://`，内网 IP 会默认走 HTTP）。默认 `cargo run` 仍只监听 `127.0.0.1`，避免无意暴露开发凭据。系统若弹出防火墙/网络权限，选择允许。`CHAT_LAN_PORT` / `CHAT_LAN_HOST` 可覆盖端口和通告地址。

```sh
cargo run --locked -p chat-server -- check-config
# 仅在 PostgreSQL 可达时执行显式迁移：
cargo run --locked -p chat-server -- migrate
cmake -S native-media-core -B build/native -DCMAKE_BUILD_TYPE=Release
cmake --build build/native --config Release
```

## 配置

服务端唯一应用配置为 `config.toml`；`CHAT_CONFIG=/path/config.toml` 指定路径。所有字段支持 `CHAT__SECTION__KEY` 覆盖，例如 `CHAT__SERVER__NAME`、`CHAT__DATABASE__MAX_CONNECTIONS`、`CHAT__STORAGE__MAX_BYTES`；数组用逗号分隔，例如 `CHAT__SERVER__ALLOWED_ORIGINS=https://a.example,https://b.example`。附件上限默认 24 MiB，允许 64 KiB..=256 MiB。不要把真实密钥写入版本库。`check-config` 仅显示校验结果，不打印秘密。

客户端配置为 App/appsettings.json，支持 `CHAT_PRODUCT_NAME` / `CHAT_CACHE_DIRECTORY`。默认缓存为 .NET LocalApplicationData 下 `chat-desktop/cache.db`。界面语言写入同目录 `preferences.json`；首次启动跟随系统（`zh*` → 简体中文，`ja*` → 日语，其余为英文），设置中可改为中文、英文或日语。`CHAT_LOCALE`（`zh-Hans` / `en` / `ja`）仅在尚未保存过语言时生效。凭据库尚未接入，SQLite 不存令牌。

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

GitHub Actions（`.github/workflows/ci.yml`）：

- protocol：Node fixtures。
- Windows/macOS/Linux：.NET Release 构建、client contract suite、CMake 构建和 C ABI check。
- Linux：Rust fmt/clippy/test/release，PostgreSQL migration 连续运行两次验证幂等，再跑 Node live 检查。
- 架构：csproj 依赖白名单、C# 空白格式、Compose 配置校验。
- 容器：构建并启动 Compose、Node live 检查、清理容器（保留命名卷的默认语义）。

远程平台结果须以实际 workflow 执行为准。三平台编译不等于三平台 GUI、分发签名或音频硬件验收。

## 性能

见 [benchmarks/README.md](benchmarks/README.md)。先测 Release 实际进程和窗口，禁止用 Debug 编译器/构建进程内存代替客户端内存。后续引入 worker 时必须合计整棵进程树。
