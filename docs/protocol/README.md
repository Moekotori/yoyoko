# Protocol v1

状态：发现和未认证 Gateway 握手已实现；以下业务路由、READY、heartbeat、resume/replay 为 Phase 1 契约，**Not implemented yet**。

## 标识和编码

UTF-8 JSON、snake_case，UUIDv7 小写标准字符串，时间 RFC3339 UTC。`protocol_version` / `api_version` 是 JSON number。u64 权限与 `seq` / `last_seq` 使用十进制字符串，避免 JS 2^53 精度损失。序列有效范围当前是 0..Int64.MaxValue，0 表示还未提交事件；无序列的控制帧使用 null。

`.NET Protocol`、`Rust chat-protocol` 与 `tests/node` 读取同一组 `fixtures/*.json`。跨语言兼容检查使用包含 >2^53 序列的样本，Node 侧断言 `seq` 必须是十进制字符串，避免 `JSON.parse` 掉进 Number。消息 type/kind 保持可扩展字符串，客户端必须容忍未知可选字段；未知事件仍推进已提交序列，但不凭空变更领域状态。

## 实例发现

`GET /.well-known/lightchat` 返回 instance_id、name、protocol_version、api_version、api、gateway、cdn、rtc。见 fixture。实例 ID 必须持久且每次部署的新实例唯一；重启不更换。

用户输入域名时默认 HTTPS；loopback 与 RFC1918/链路本地地址可 HTTP，便于局域网开发。公网明文 HTTP 仍拒绝。发现不传凭据。API / Gateway 与输入同源；CDN/RTC 可独立域。发现响应在本机/局域网 Host 下按请求地址通告 API 与 Gateway，避免 localhost 与局域网 IP 互相判成不同源。固定发现路径是协议标识，不是品牌配置。major 不匹配须显示明确不兼容提示。minor 增量仅新增可选字段，不静默改变字段语义。

## REST 边界

已实现：`GET /health/live`、发现端点。`/health/live` 只证明 API 进程存活，不证明 Postgres/Redis/S3/RTC 可用；业务 readiness 将在 Phase 1 引入。

计划 `/api/v1`：

| 方法与资源 | 用途 |
| --- | --- |
| POST /auth/register, /auth/login, /auth/refresh, /auth/logout | 实例内账号与会话 |
| GET /users/me | 当前实例身份 |
| GET/POST /servers | 社区列表/创建 |
| GET/POST /servers/{id}/channels | 频道列表/创建 |
| GET/POST /channels/{id}/messages | before 游标分页/发送（Idempotency-Key） |
| PATCH/DELETE /channels/{id}/messages/{message_id} | 编辑/删除 |
| POST /attachments | 经校验的流式上传 / 授权对象存储流程 |
| POST /channels/{id}/rtc-token | 校验 ConnectVoice/Speak/Stream 后签发短期 LiveKit token |

业务接口使用 Bearer access token；refresh rotation、Argon2id、限流、body/field limits 和服务端授权检查在 Phase 1 接入。错误 envelope `{ "code": "...", "message": "..." }`；不得泄露数据库或密钥。普通 WebSocket 不接管 CRUD、文件或媒体数据。

## Gateway

当前：升级 WebSocket → `hello` → 最多等待 5 秒 identify → 关闭。版本不匹配 4406，认证未实现 4401，数据无效 4400，超时 4408。不会返回 READY 或假在线状态。最大帧/消息 64 KiB。

```json
{"op":"hello","event":null,"seq":null,"data":{"protocol_version":1,"heartbeat_interval_ms":30000}}
```

```json
{"op":"identify","event":null,"seq":null,"data":{"protocol_version":1,"access_token":"<in-memory-token>"}}
```

Phase 1：验证 token 和 origin（原生无 Origin 的客户端仍需 token），分配 session_id，发 READY。session 内 sequence 单调递增。连接不能自己声明任何订阅权限。

```json
{"op":"resume","event":null,"seq":null,"data":{"protocol_version":1,"access_token":"<token>","session_id":"<opaque>","last_seq":"123"}}
```

- 只有已在 SQLite transaction 提交的事件可更新 resume cursor。
- 服务端以原 session 的权限校验和重放，客户端幂等应用 message ID/revision；忽略 seq ≤ cursor 的重复帧。
- 发现缺口暂停继续提交，发 resume；窗口失效返回 invalid_session，按有界分页重新同步有权访问的状态。
- 30 秒 heartbeat，2 个周期无 ACK 断开；指数退避 0.5–30 秒基值、±25% jitter，上限 37.5 秒。取消 / 注销立即停止。
- READY / outbox commit / replay ordering 原子语义、15 分钟或 10,000 事件的有界 retention 在 Phase 1 实现，不创建无界内存事件队列。
- reconnect cursor 必须按实例和账号独立。Redis 驱逐造成 replay 缺失时走 invalid_session，不能谎称恢复成功。

计划事件：READY、MESSAGE_CREATE/UPDATE/DELETE、CHANNEL_CREATE/UPDATE/DELETE、SERVER_CREATE/UPDATE、MEMBER_JOIN/LEAVE、PRESENCE_UPDATE、TYPING_START、VOICE_STATE_UPDATE。typing 不持久化；presence/voice 高频状态不写 PostgreSQL。消息含 text/system/encrypted、reply、mention、attachment、embed、reaction；见 Message DTO。

官方与自托管走同一套协议与版本协商；无官方专用权限后门。当前不做 federation；未来 Web/移动端不需依赖 C# 逻辑。
