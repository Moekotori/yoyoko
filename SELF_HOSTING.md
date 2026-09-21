# Self hosting

当前 Compose 是 **Phase 1 本机/内网一键堆栈**，不是已完成的公网生产安装。一条命令即可拉起 API/Gateway、PostgreSQL、Redis、LiveKit 和一次性 migration。默认发布到回环、使用开发凭据、明文 HTTP。公网 TLS、TURN、MinIO 业务接入、安全默认值和安装器属于 Phase 6。

## 一行启动

安装 Docker Engine/Desktop 与 Compose v2 后，在仓库根目录：

```sh
docker compose up -d --build
```

Windows 也可双击 `up.cmd`，macOS/Linux 用 `up.command` 或 `npm run up`。首次构建镜像要编译 Rust，需要几分钟；之后再执行同一命令会复用 `chat-api:local`。

```sh
docker compose ps
curl http://127.0.0.1:8080/health/ready
curl http://127.0.0.1:8080/.well-known/lightchat
```

就绪后桌面客户端连接 `http://localhost:8080`。停止：

```sh
docker compose down
```

`down` 不删命名卷；`down -v` 会清空 Postgres 和附件，正常不要用。

局域网把 8080/7880 发到所有网卡：

```sh
CHAT_PUBLISH=0.0.0.0 docker compose up -d
# 或 npm run up -- --lan
```

发现接口会按请求 `Host` 改写本机/内网 API 与 Gateway 地址。

## 堆栈里有什么

| 服务 | 作用 |
| --- | --- |
| api | Rust API + Gateway，监听容器内 `0.0.0.0:8080` |
| migrate | 同一镜像，对 Postgres 跑 sqlx migration 后退出 |
| postgres | 业务事实源；Compose 覆盖 `CHAT__DATABASE__URL` |
| redis | 给 LiveKit 用；聊天进程尚未连接 |
| minio | 可选 profile `s3`；社区镜像已离开 Docker Hub，**聊天附件仍写 API 本地卷** |
| livekit | 语音 SFU；桌面 worker / 网页访客连主机 `localhost:7880` |

API 与 migrate 共用 `chat-api:local`。对象文件在 `api-data` 卷的 `/app/data/objects`。Postgres 与 Redis 不映射主机端口。

## 配置

应用业务配置仍是根目录 `config.toml`，容器里只读挂载，字段可用 `CHAT__SECTION__KEY` 覆盖。Compose 只改容器内部地址、库连接和对象目录。LiveKit 仍用 `deploy/livekit.yaml`；Postgres/MinIO 用各自环境变量。生产安装器在 Phase 6 再统一生成这些配置。

每个独立实例应生成唯一 UUIDv7 写入 `server.instance_id`；同一实例升级/恢复保持它不变。`storage.public_url` 与 `rtc.public_url` 是客户端可达的外部 URL，不是 Docker 服务名。

生产之前需同步替换数据库、MinIO、LiveKit 两侧凭据，并配置 HTTPS/WSS 反代、受信任证书、LiveKit 公网 IP/UDP/TURN、防火墙和备份。不要只改端口绑定就宣称上线。

LiveKit 开发范围为 50000–50100/UDP；生产按并发与 NAT 重做。参考 [官方部署](https://docs.livekit.io/transport/self-hosting/deployment/) 与 [端口说明](https://docs.livekit.io/transport/self-hosting/ports-firewall/)。

## 数据升级和备份

```sh
mkdir -p backups
docker compose exec -T postgres pg_dump -U chat -Fc chat > backups/chat.dump
```

另行备份 `api-data` 对象、config、instance_id 与密钥；禁止把备份提交 Git。恢复时先在隔离实例演练。数据库版本只通过 sqlx migration 向前推进。Compose 更新会执行一次性 migrate。

## 验证边界

本机可用 Docker 时，`docker compose up -d --build` 应通过 `/health/ready` 与发现。这仍是开发拓扑，不代表公网、TLS 或三平台生产验收。没有 Docker 的机器继续用 `cargo run -p chat-server`（`local:` 文件库）。
