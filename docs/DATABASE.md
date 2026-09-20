# Persistence

## PostgreSQL：事实源

`database/migrations/0001_initial.sql` 是可执行的版本化初始 schema。公开实体由 service 分配 UUIDv7，不使用自增公开 ID。

| 表 | 所属业务与约束 |
| --- | --- |
| users / sessions | 实例内账号、Argon2id PHC、refresh hash 与过期/撤销时间；`avatar_id` 指向未绑定消息的附件，`avatar_animated` 标记 GIF/动态 WebP/APNG |
| servers / members | 社区与成员；owner、membership 外键；`0005_moderation.sql` 增加 `blocked_words` JSONB 与 `cooldown_seconds`（0–600） |
| roles / member_roles | u64 permission 使用 NUMERIC(20,0)，server 范围复合外键 |
| channels | text/voice、server 索引、显示顺序；语音频道 `audio_quality` 上限（standard/high/very_high/studio，默认 studio） |
| channel_role_overrides / channel_member_overrides | allow/deny 位，禁止跨 server 引用 |
| messages | UUIDv7、kind、content、reply、edit/delete、JSON embed/encrypted envelope |
| message_mentions / reactions | 标准化关联、复合唯一键 |
| attachments | 随机 UUID object key、显示名、MIME、大小、缩略图 key；表内硬顶 256 MiB，实际上限由 `storage.max_bytes` 执行 |

消息分页：`WHERE channel_id=$1 AND id<$before ORDER BY id DESC LIMIT $limit`。同频道 reply 外键避免跨频道回复。UUIDv7 提供稳定可排序 keyset，不承诺跨节点严格因果时钟；真正事件顺序以 Gateway seq 为准。

全文检索先用 PostgreSQL GIN + simple to_tsvector；无 Elasticsearch，中文分词质量需要 Phase 2 单独评估。加密消息 content 必须为 NULL，不进入明文索引；只预留 envelope，不宣称已实现 E2EE。

临时状态不写 Postgres：presence 使用 Redis TTL，typing 短期 Gateway 事件，voice state 使用 Redis/内存。S3 只存内容，DB 存元数据。上传的 size/MIME/扩展名需在服务端实施，不把文件名作为路径。

`0002_chat.sql` 增加 outbox、gateway_sessions、idempotency、invites，并允许 pending attachment。发送消息时同一事务写入 message + outbox，提交后再推 Gateway。Outbox 保留约 15 分钟或每用户 10,000 条。本机默认 `local:` JSON 存储实现同一语义；Postgres 由 Compose/CI 使用。

迁移显式执行 `chat-server migrate`；正常 API 启动不暗中改库。sqlx migration 表记录版本与校验和。发布前备份，禁止修改已经部署的 migration；回退优先 forward fix，不提供破坏性自动 down migration。

## SQLite：可丢弃缓存

目前实现 `instances / accounts / messages / settings` 表。消息主键 `(instance_id, account_id, id)`，查询索引额外包含 channel_id。消息 wire payload 有版本协议约束，不是凭据容器。

- InstanceStore 持久化和恢复实例，不依赖网络。
- MessageCache 以 account + instance 分区，参数化 SQL，单页 ≤100，取 limit+1 判断下一页。
- 每账号/频道写入时只保留最近 1000 条；删除和裁剪同一事务。
- SQLite WAL，foreign_keys=on，busy timeout 3 秒，短连接、无池，2 MiB 页缓存。
- 所有 SQLite 工作放在 worker thread；Microsoft.Data.Sqlite 的 async API 不会让底层磁盘调用真正异步。
- PRAGMA user_version 管理缓存版本。遇到比客户端新的 schema 拒绝写入。
- `PurgeAsync` 提供注销时删除该账号消息缓存的端口，实际注销流程尚未实现。

Phase 1 扩展缓存表设计：

```text
servers:     PK(instance_id, account_id, id), name, owner_id
channels:    PK(instance_id, account_id, id), server_id, kind, name
users:       PK(instance_id, account_id, id), display_name, avatar_thumbnail
members:     PK(instance_id, account_id, server_id, user_id)
roles:       PK(instance_id, account_id, id), server_id, permissions
attachments: PK(instance_id, account_id, id), message_id, metadata, thumbnail_cache_key
sync_state:  PK(instance_id, account_id), session_id, last_committed_seq
```

这些表与对应 sync adapter **Not implemented yet**，不生成无用途空表冒充已支持。Phase 1 必须将事件变更和 `last_committed_seq` 放在同一个事务，并在 UI 通知前提交。缓存不是事实源；权限撤销、注销和服务端删除必须使本地数据失效。
