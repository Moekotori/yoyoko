# Protocol v1

状态：发现、认证 REST、社区/频道、消息分页与发送、图片上传、Gateway READY / heartbeat / resume 已实现。消息编辑/删除、mention、reaction 仍为 **Not implemented yet**。

## 标识和编码

UTF-8 JSON、snake_case，UUIDv7 小写标准字符串，时间 RFC3339 UTC。`protocol_version` / `api_version` 是 JSON number。u64 权限与 `seq` / `last_seq` 使用十进制字符串，避免 JS 2^53 精度损失。序列有效范围当前是 0..Int64.MaxValue，0 表示还未提交事件；无序列的控制帧使用 null。

`.NET Protocol`、`Rust chat-protocol` 与 `tests/node` 读取同一组 `fixtures/*.json`。跨语言兼容检查使用包含 >2^53 序列的样本，Node 侧断言 `seq` 必须是十进制字符串，避免 `JSON.parse` 掉进 Number。消息 type/kind 保持可扩展字符串，客户端必须容忍未知可选字段；未知事件仍推进已提交序列，但不凭空变更领域状态。

## 实例发现

`GET /.well-known/lightchat` 返回 instance_id、name、protocol_version、api_version、api、gateway、cdn、rtc。见 fixture。实例 ID 必须持久且每次部署的新实例唯一；重启不更换。

用户输入域名时默认 HTTPS；loopback 与 RFC1918/链路本地地址可 HTTP，便于局域网开发。公网明文 HTTP 仍拒绝。发现不传凭据。API / Gateway 与输入同源；CDN/RTC 可独立域。发现响应在本机/局域网 Host 下按请求地址通告 API 与 Gateway，避免 localhost 与局域网 IP 互相判成不同源。固定发现路径是协议标识，不是品牌配置。major 不匹配须显示明确不兼容提示。minor 增量仅新增可选字段，不静默改变字段语义。

## REST 边界

发现响应含 `max_attachment_bytes` 与 `max_attachments_per_message`（默认 24 MiB、每条最多 4 个）。实例用 `storage.max_bytes`（64 KiB..=256 MiB，`CHAT__STORAGE__MAX_BYTES`）自定义上限。`/health/live` 只证明 API 进程存活，不证明 Postgres/Redis/S3/RTC 可用。

计划 `/api/v1`：

| 方法与资源 | 用途 |
| --- | --- |
| POST /auth/register, /auth/login, /auth/refresh, /auth/logout | 实例内账号与会话 |
| GET /users/me | 当前实例身份 |
| PATCH /users/me | 更新用户名、显示名、头像（avatar_id 或 clear_avatar） |
| GET/POST /servers | 社区列表/创建 |
| PATCH /servers/{id}/moderation | 社区屏蔽词与发言冷却（owner 或 MANAGE_MESSAGES）。`blocked_words` 最多 200 条、每词 1–32 字符；`cooldown_seconds` 0–600 |
| GET/POST /servers/{id}/channels | 频道列表/创建 |
| GET/POST /channels/{id}/messages | before 游标分页/发送（Idempotency-Key） |
| PATCH/DELETE /channels/{id}/messages/{message_id} | 编辑/删除（**Not implemented yet**） |
| POST /attachments | 流式上传；校验大小、扩展名与 MIME；图片生成 256px 缩略图 |
| GET /attachments/{id}/content | 签名 URL 或登录用户下载；存储 key 为随机 UUID |
| GET /attachments/{id}/thumbnail | 图片 JPEG 缩略图 |
| POST /channels/{id}/rtc-token | 校验 ConnectVoice 后签发短期 LiveKit token（已实现；可发布取决于 Speak） |
| POST /channels/{id}/voice/join | 加入语音频道；可带 `audio_quality`（standard/high/very_high/studio），服务端按频道上限钳制 |
| PATCH /channels/{id} | 拥有 MANAGE_CHANNEL 的成员修改可选 `name` 和语音频道 `audio_quality` 上限 |
| DELETE /channels/{id} | MANAGE_CHANNEL 权限；删除频道及消息，成功返回 204 |
| POST /voice/leave、PATCH /voice/state | 离开；更新 mute/deafen 与可选发送音质。PATCH 不重新签发 LiveKit token、不让成员退出频道 |

业务接口使用 Bearer access token；refresh rotation、Argon2id、限流、body/field limits 和服务端授权检查在 Phase 1 接入。错误 envelope `{ "code": "...", "message": "..." }`，冷却拒绝时额外带 `retry_after_seconds`。发送消息可能返回 `blocked_word`（400）或 `cooldown`（429）。不得泄露数据库或密钥。普通 WebSocket 不接管 CRUD、文件或媒体数据。Server DTO 含 `blocked_words` 与 `cooldown_seconds`（默认 `[]` / `0`）。

客户端已接通既有 `POST /servers/{id}/channels`：请求 `{name, kind, audio_quality}`，`kind` 为 `text` 或 `voice`，客户端创建时 `audio_quality` 传 null，采用服务端默认值。创建仍由服务端校验社区 owner 身份，成功后同步社区缓存。没有 DTO 或协议版本变更。

## Gateway

当前：升级 WebSocket → `hello` → identify 或 resume。版本不匹配 4406，令牌无效 4401，数据无效 4400，超时 4408，resume 失效 4409。identify 成功后发 READY 并推送 MESSAGE_CREATE。最大帧/消息 64 KiB。

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

计划事件：READY、MESSAGE_CREATE/UPDATE/DELETE、CHANNEL_CREATE/UPDATE/DELETE、SERVER_CREATE/UPDATE、MEMBER_JOIN/LEAVE、USER_UPDATE、PRESENCE_UPDATE、TYPING_START、VOICE_STATE_UPDATE。User 含可选 `avatar`（download/thumbnail URL、`animated`）；JPEG/PNG/GIF/WebP，GIF/APNG/动态 WebP 为 animated，上限 8 MiB、边长 4096。`VOICE_STATE_UPDATE` 已实现：`channel_id` 为 null 表示离开。语音状态含 `audio_quality`。音质档位：`standard` 48 kHz 单声道 64 kbps、`high` 立体声 128 kbps、`very_high` 立体声 384 kbps、`studio` 510 kbps（Opus 上限）。用户在频道上限内自选发送音质；加入响应带 `audio` 编码参数与 `max_audio_quality`。降低上限时服务端钳制已在频道内的发送档位并广播 `VOICE_STATE_UPDATE`。typing 不持久化；presence/voice 高频状态不写 PostgreSQL。语音状态保存在 API 进程内存中；单进程模块化单体足够，多节点需 Redis。消息含 text/system/encrypted、reply、mention、attachment、embed、reaction；见 Message DTO。

官方与自托管走同一套协议与版本协商；无官方专用权限后门。当前不做 federation；未来 Web/移动端不需依赖 C# 逻辑。

### 频道管理增量（2026-09-21）

`api_version=1` / `protocol_version=1` 保持不变：PATCH 新增可选 `name`（trim 后 1–100 UTF-8 字节），兼容只提交 `audio_quality` 的客户端。名称/音质在同一存储操作更新；文字频道不能指定音质。创建仍限定社区所有者，修改/删除由服务端校验 MANAGE_CHANNEL。桌面目前仅向所有者显示管理入口，完整角色管理仍未实现。

`CHANNEL_CREATE` / `CHANNEL_UPDATE` / `CHANNEL_DELETE` 均已实现，data 为 Channel，seq 为十进制字符串。客户端刷新并提交 SQLite 社区快照后推进游标，删除/失去访问权的频道清除消息缓存、打开标签和选中状态；所处语音频道消失时退出媒体会话。旧客户端可忽略新增 DELETE 事件，但需重新同步才能移除频道。删除不等于立即回收对象存储中的附件文件，附件物理清理由独立保留策略处理（目前未实现）。

共享示例：`fixtures/channel-patch.json`、`fixtures/channel-delete.json`。
