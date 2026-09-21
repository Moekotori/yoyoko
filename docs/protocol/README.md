# Protocol v1

状态：发现、认证 REST、社区/频道、1:1 私信、消息分页与发送、作者编辑正文、作者或 `MANAGE_MESSAGES` 软删除、同频道回复、图片上传、Gateway READY / heartbeat / resume 已实现。发送与编辑会解析 `@username` / 显示名写入 `mentions`，解析 `@everyone` 写入 `mention_everyone`，解析 `@here` 写入 `mention_here` 并把当前 Gateway 在线成员（最多 256）写入 `mentions`。`reply_to` 会把被回复作者加入 `mentions`。`POST /channels/{id}/typing` 广播无序列的 `TYPING_START`。reaction、系统通知与 presence 仍为 **Not implemented yet**。未读/静音/草稿是客户端缓存，不进协议。

## 标识和编码

UTF-8 JSON、snake_case，UUIDv7 小写标准字符串，时间 RFC3339 UTC。`protocol_version` / `api_version` 是 JSON number。u64 权限与 `seq` / `last_seq` 使用十进制字符串，避免 JS 2^53 精度损失。序列有效范围当前是 0..Int64.MaxValue，0 表示还未提交事件；无序列的控制帧使用 null。

`.NET Protocol`、`Rust chat-protocol` 与 `tests/node` 读取同一组 `fixtures/*.json`。跨语言兼容检查使用包含 >2^53 序列的样本，Node 侧断言 `seq` 必须是十进制字符串，避免 `JSON.parse` 掉进 Number。消息 type/kind 保持可扩展字符串，客户端必须容忍未知可选字段；未知事件仍推进已提交序列，但不凭空变更领域状态。

## 实例发现

`GET /.well-known/lightchat` 返回 instance_id、name、protocol_version、api_version、api、gateway、cdn、rtc。见 fixture。实例 ID 必须持久且每次部署的新实例唯一；重启不更换。

用户输入域名时默认 HTTPS；loopback 与 RFC1918/链路本地地址可 HTTP，便于局域网开发。公网明文 HTTP 仍拒绝。发现不传凭据。API / Gateway 与输入同源；CDN/RTC 可独立域。发现响应在本机/局域网 Host 下按请求地址通告 API 与 Gateway，避免 localhost 与局域网 IP 互相判成不同源。固定发现路径是协议标识，不是品牌配置。major 不匹配须显示明确不兼容提示。minor 增量仅新增可选字段，不静默改变字段语义。

桌面可复制 `{origin}/join/{invite_code}` 作为邀请链接（例如 `http://192.168.1.10:8080/join/ABCD2345`）。这不是服务端路由；客户端去掉路径后仍走发现，再用现有 `POST /servers/join` 提交社区 `invite_code`。短码本身不能在没有实例地址的情况下找到服务器。

`{origin}/voice/{channel_id}` 是服务端路由：打开后只进入该语音频道。`GET /api/v1/voice/rooms/{id}` 返回频道名与人数；文字频道与未知 ID 一律 404，不泄露其它频道。`POST /api/v1/voice/rooms/{id}/guest` 用显示名签发语音范围 access token（`v2`），只允许该频道的 join/leave/state/voice-states。访客不是社区成员，不能列频道、读消息或连 Gateway。见 `fixtures/voice-room.json`、`fixtures/voice-guest-join.json`。`api_version` 仍为 1。

## REST 边界

发现响应含 `max_attachment_bytes` 与 `max_attachments_per_message`（默认 24 MiB、每条最多 4 个）。实例用 `storage.max_bytes`（64 KiB..=256 MiB，`CHAT__STORAGE__MAX_BYTES`）自定义上限。`/health/live` 只证明 API 进程存活，不证明 Postgres/Redis/S3/RTC 可用。

计划 `/api/v1`：

| 方法与资源 | 用途 |
| --- | --- |
| POST /auth/register, /auth/login, /auth/refresh, /auth/logout | 实例内账号与会话 |
| GET /users/me | 当前实例身份 |
| PATCH /users/me | 更新用户名、显示名、头像与资料横幅（avatar_id / clear_avatar，banner_id / clear_banner） |
| GET/POST /servers | 社区列表/创建 |
| PATCH /servers/{id}/moderation | 社区屏蔽词与发言冷却（owner 或 MANAGE_MESSAGES）。`blocked_words` 最多 200 条、每词 1–32 字符；`cooldown_seconds` 0–600 |
| GET/POST /servers/{id}/channels | 频道列表/创建 |
| GET/POST /dms | 列出当前账号的 1:1 私信；`{recipient_id}` 打开或复用私信频道 |
| GET/POST /channels/{id}/messages | before 游标分页/发送（Idempotency-Key）；`reply_to` 须为同频道未删除消息，被回复作者计入 `mentions` |
| PATCH /channels/{id}/messages/{message_id} | 作者编辑正文；写入 `edited_at`，按当前成员解析 `@username` / 显示名填 `mentions`、解析 `@everyone` / `@here` 填对应布尔，广播 `MESSAGE_UPDATE` |
| DELETE /channels/{id}/messages/{message_id} | 作者或拥有 `MANAGE_MESSAGES` 的成员软删除；写入 `deleted_at`，广播 `MESSAGE_DELETE`（`id` + `channel_id`），成功 204 |
| POST /channels/{id}/typing | 需要 `SEND_MESSAGE`；204 后向同社区其他成员广播无 `seq` 的 `TYPING_START`，不入 outbox；10 秒内最多 8 次 |
| POST /attachments | 流式上传；校验大小、扩展名与 MIME；图片生成 256px 缩略图 |
| GET /attachments/{id}/content | 签名 URL 或登录用户下载；存储 key 为随机 UUID |
| GET /attachments/{id}/thumbnail | 图片 JPEG 缩略图 |
| POST /channels/{id}/rtc-token | 校验 ConnectVoice 后签发短期 LiveKit token（已实现；可发布取决于 Speak） |
| POST /channels/{id}/voice/join | 加入语音频道；可带 `audio_quality`（standard/high/very_high/studio），服务端按频道上限钳制 |
| PATCH /channels/{id} | 拥有 MANAGE_CHANNEL 的成员修改可选 `name` 和语音频道 `audio_quality` 上限 |
| DELETE /channels/{id} | MANAGE_CHANNEL 权限；删除频道及消息，成功返回 204 |
| POST /voice/leave、PATCH /voice/state | 离开；更新 mute/deafen 与可选发送音质。PATCH 不重新签发 LiveKit token、不让成员退出频道 |
| GET /voice/rooms/{id}、POST /voice/rooms/{id}/guest | 网页语音预览与访客加入；令牌仅限该语音频道 |

业务接口使用 Bearer access token；refresh rotation、Argon2id、限流、body/field limits 和服务端授权检查在 Phase 1 接入。错误 envelope `{ "code": "...", "message": "..." }`，冷却拒绝时额外带 `retry_after_seconds`。发送消息可能返回 `blocked_word`（400）或 `cooldown`（429）。不得泄露数据库或密钥。普通 WebSocket 不接管 CRUD、文件或媒体数据。Server DTO 含 `blocked_words` 与 `cooldown_seconds`（默认 `[]` / `0`）。

客户端已接通既有 `POST /servers/{id}/channels`：请求 `{name, kind, audio_quality}`，`kind` 为 `text` 或 `voice`，客户端创建时 `audio_quality` 传 null，采用服务端默认值。创建仍由服务端校验社区 owner 身份，成功后同步社区缓存。

### 私信（2026-09-21）

`api_version=1` / `protocol_version=1` 保持不变：Channel 新增可选 `participants`，`server_id` 对 `kind=dm` 可省略。社区频道仍带 `server_id`，旧客户端可忽略私信字段。`POST /api/v1/dms` 对已共享社区的用户 get-or-create 一条 `kind=dm` 频道，两人复用现有 `/channels/{id}/messages` 与 Gateway `CHANNEL_CREATE` / `MESSAGE_*`。READY 的 `channels` 含当前账号的私信。私信不做社区屏蔽词、冷却或角色权限；参与者可查看和发送。不能给自己开私信；未同社区且尚无会话则 403。见 `fixtures/dm-open.json`。

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

- 只有已在 SQLite transaction 提交的事件可更新 resume cursor；消息 upsert/删除与 `last_committed_seq` 同事务。
- 服务端以原 session 的权限校验和重放，客户端幂等应用 message ID/revision；忽略 seq ≤ cursor 的重复帧。
- 发现缺口暂停继续提交并重连 resume；再次缺口或 `invalid_session` 清 cursor 后 identify，只按有界分页刷新当前打开的频道，不全量拉历史。
- 30 秒 heartbeat，客户端 2 个周期无 ACK 断开网关；指数退避 0.5–30 秒基值、±25% jitter，上限 37.5 秒。取消 / 注销立即停止。
- READY / outbox commit / replay ordering：服务端 15 分钟或 10,000 事件有界 retention；客户端单调推进 cursor，不创建无界内存事件队列。
- reconnect cursor 必须按实例和账号独立。Redis 驱逐造成 replay 缺失时走 invalid_session，不能谎称恢复成功。

计划事件：READY、MESSAGE_CREATE/UPDATE/DELETE、CHANNEL_CREATE/UPDATE/DELETE、SERVER_CREATE/UPDATE、MEMBER_JOIN/LEAVE、USER_UPDATE、PRESENCE_UPDATE、TYPING_START、VOICE_STATE_UPDATE。MESSAGE_CREATE、MESSAGE_UPDATE、MESSAGE_DELETE 与 `TYPING_START` 已实现。`POST /channels/{id}/typing` 校验 `SEND_MESSAGE` 后向其他成员广播 `{user_id,channel_id,display_name}`，不入 outbox、seq 为 null、客户端 8 秒后清除；同一用户 10 秒内最多 8 次。User 含可选 `avatar` 与 `banner`（download/thumbnail URL、`animated`）；JPEG/PNG/GIF/WebP，GIF/APNG/动态 WebP 为 animated，上限 8 MiB、边长 4096。横幅与头像共用上传与校验规则。`VOICE_STATE_UPDATE` 已实现：`channel_id` 为 null 表示离开。语音状态含 `audio_quality`。音质档位：`standard` 48 kHz 单声道 64 kbps、`high` 立体声 128 kbps、`very_high` 立体声 384 kbps、`studio` 510 kbps（Opus 上限）。用户在频道上限内自选发送音质；加入响应带 `audio` 编码参数与 `max_audio_quality`。降低上限时服务端钳制已在频道内的发送档位并广播 `VOICE_STATE_UPDATE`。typing 不持久化；presence/voice 高频状态不写 PostgreSQL。语音状态保存在 API 进程内存中；单进程模块化单体足够，多节点需 Redis。消息含 text/system/encrypted、reply、mention、`mention_everyone`、`mention_here`、attachment、embed、reaction；见 Message DTO。`mention_everyone` / `mention_here` 为可选布尔，缺省 false。`@here` 按 Gateway 连接判定，不是 presence。

官方与自托管走同一套协议与版本协商；无官方专用权限后门。当前不做 federation；未来 Web/移动端不需依赖 C# 逻辑。

### 频道管理增量（2026-09-21）

`api_version=1` / `protocol_version=1` 保持不变：PATCH 新增可选 `name`（trim 后 1–100 UTF-8 字节），兼容只提交 `audio_quality` 的客户端。名称/音质在同一存储操作更新；文字频道不能指定音质。创建仍限定社区所有者，修改/删除由服务端校验 MANAGE_CHANNEL。桌面目前仅向所有者显示管理入口，完整角色管理仍未实现。

`CHANNEL_CREATE` / `CHANNEL_UPDATE` / `CHANNEL_DELETE` 均已实现，data 为 Channel，seq 为十进制字符串。客户端刷新并提交 SQLite 社区快照后推进游标，删除/失去访问权的频道清除消息缓存、打开标签和选中状态；所处语音频道消失时退出媒体会话。旧客户端可忽略新增 DELETE 事件，但需重新同步才能移除频道。删除不等于立即回收对象存储中的附件文件，附件物理清理由独立保留策略处理（目前未实现）。

共享示例：`fixtures/channel-patch.json`、`fixtures/channel-delete.json`、`fixtures/message-delete.json`、`fixtures/typing-start.json`。
