# Self hosting foundation

当前 Compose 是 **Phase 0 本地开发部署基础**，不是已完成的公网聊天服务。认证、聊天、对象上传、RTC token 与生产 TLS 尚未实现。默认仅映射 loopback，样例凭据仅用于本地开发。

## 启动

安装 Docker Engine/Desktop 与 Compose v2 后，从根目录：

```sh
docker compose up -d --build
docker compose ps
curl http://localhost:8080/.well-known/lightchat
```

包含：Rust API/Gateway、一次性 migration、PostgreSQL、Redis、MinIO、LiveKit。API 等待 migration 成功；migration 等待 Postgres 健康。Postgres 与 Redis 不映射主机端口。数据库/对象存储使用命名卷；`docker compose down` 不删卷，`down -v` 会销毁数据，正常操作不要使用。

当前 MinIO 仅启动服务，业务 bucket 创建和 policy 在 Phase 6 自动化；Phase 0 不做上传，因此不创建 public bucket。LiveKit 仅启动配置基础，尚未接入客户端，端口监听不代表语音可用。

## 配置

应用业务配置集中在 `config.toml`，支持 `CHAT__SECTION__KEY` 环境覆盖。Compose 中只覆盖容器内部地址。LiveKit 自己仍使用 deploy/livekit.yaml，Postgres/MinIO 用容器环境变量；生产安装器在 Phase 6 统一生成这些服务的配置，当前不谎称一个业务 config 能自动修改所有基础设施账号。

每个独立实例应生成唯一 UUIDv7 写入 server.instance_id；同一实例升级/恢复保持它不变。部署域名进入 public_url，发现自动生成同源 api/gateway 地址；storage.public_url 与 rtc.public_url 是客户端可达的外部 URL，不是 Docker 服务名。

应用配置实例：

```toml
[server]
instance_id = "<new UUIDv7>"
name = "Friends"
listen = "0.0.0.0:8080"
public_url = "https://chat.example.com"
allowed_origins = ["https://chat.example.com"]
```

生产之前需同步替换数据库、MinIO、LiveKit 两侧凭据，并配置 HTTPS/WSS 反代、受信任证书、LiveKit 公网 IP/UDP/TURN、防火墙和备份。当前模板有意保持回环开发拓扑；不要只改端口绑定就宣称上线。

LiveKit 的公开 UDP/ICE 和 TCP 端口应依实际部署配置；参考[官方部署文档](https://docs.livekit.io/transport/self-hosting/deployment/)与[端口说明](https://docs.livekit.io/transport/self-hosting/ports-firewall/)。当前开发范围为 50000–50100/UDP；生产需要按并发、网络与 NAT 重新规划。

## 数据升级和备份

```sh
mkdir -p backups
docker compose exec -T postgres pg_dump -U chat -Fc chat > backups/chat.dump
```

另行备份 MinIO 对象、config、instance_id 与密钥；禁止把备份提交 Git。恢复时先在隔离实例演练，保留原始数据快照。数据库版本只通过 sqlx migration 向前推进，必须部署匹配的服务端版本。Compose 更新会执行一次性 migrate；重大升级应先停业务写入并备份。

## 验证边界

当前开发机没有 Docker，本次不将 Compose、Postgres migration、MinIO 或 LiveKit 容器启动标记为实测通过。CI 已加入实际容器构建/启动与 migration 检查，运行结果见对应 GitHub Actions。公网部署留到 Phase 6，此次没有连接 SSH 或修改任何服务器。
