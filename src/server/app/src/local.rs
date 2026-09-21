mod channel;

use crate::store::{
    GatewaySession, NewServer, OutboxEvent, PermissionSnapshot, SessionRecord, Store, StoreError,
};
use async_trait::async_trait;
use chat_domain::{
    channel::{Channel, ChannelKind, Server},
    message::{Attachment, Message},
    permission::Permissions,
    user::User,
};
use chrono::Utc;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{
    collections::{BTreeMap, HashMap, HashSet},
    path::PathBuf,
};
use tokio::sync::Mutex;
use uuid::Uuid;

#[derive(Serialize, Deserialize, Clone)]
struct UserRow {
    id: Uuid,
    username: String,
    display_name: String,
    password_hash: String,
    #[serde(default)]
    avatar_id: Option<Uuid>,
    #[serde(default)]
    avatar_animated: bool,
}
#[derive(Serialize, Deserialize, Clone)]
struct SessionRow {
    id: Uuid,
    user_id: Uuid,
    refresh_hash: [u8; 32],
    expires_at: i64,
    revoked: bool,
}
#[derive(Serialize, Deserialize, Clone)]
struct ServerRow {
    id: Uuid,
    name: String,
    owner_id: Uuid,
    invite_code: String,
    #[serde(default)]
    blocked_words: Vec<String>,
    #[serde(default)]
    cooldown_seconds: u32,
}
#[derive(Serialize, Deserialize, Clone)]
struct RoleRow {
    id: Uuid,
    server_id: Uuid,
    permissions: u64,
    is_base: bool,
}
#[derive(Serialize, Deserialize, Clone)]
struct ChannelRow {
    id: Uuid,
    server_id: Uuid,
    name: String,
    kind: String,
    #[serde(default = "default_audio_quality")]
    audio_quality: String,
}
fn default_audio_quality() -> String {
    chat_domain::voice::AudioQuality::DEFAULT.as_str().into()
}
#[derive(Serialize, Deserialize, Clone)]
struct MessageRow {
    id: Uuid,
    channel_id: Uuid,
    author_id: Uuid,
    kind: String,
    content: Option<String>,
    created_at: String,
    edited_at: Option<String>,
    reply_to: Option<Uuid>,
    #[serde(default)]
    mentions: Vec<Uuid>,
}
#[derive(Serialize, Deserialize, Clone)]
struct AttachmentRow {
    attachment: AttachmentDto,
    uploader_id: Uuid,
    message_id: Option<Uuid>,
}
#[derive(Serialize, Deserialize, Clone)]
struct AttachmentDto {
    id: Uuid,
    file_name: String,
    mime_type: String,
    size: u64,
    object_key: Uuid,
    thumbnail_key: Option<Uuid>,
}
#[derive(Serialize, Deserialize, Clone)]
struct OutboxRow {
    seq: i64,
    user_id: Uuid,
    event: String,
    payload: Value,
    created_at: i64,
}
#[derive(Serialize, Deserialize, Clone)]
struct GwRow {
    id: Uuid,
    user_id: Uuid,
    last_seq: i64,
    expires_at: i64,
}
#[derive(Serialize, Deserialize, Default)]
struct Snapshot {
    users: Vec<UserRow>,
    sessions: Vec<SessionRow>,
    servers: Vec<ServerRow>,
    members: Vec<(Uuid, Uuid)>,
    roles: Vec<RoleRow>,
    member_roles: Vec<(Uuid, Uuid, Uuid)>,
    channels: Vec<ChannelRow>,
    invites: Vec<(String, Uuid, Uuid)>,
    messages: Vec<MessageRow>,
    attachments: Vec<AttachmentRow>,
    outbox: Vec<OutboxRow>,
    outbox_seq: i64,
    gateway: Vec<GwRow>,
    idempotency: Vec<(Uuid, String, Uuid)>,
}

struct Inner {
    users: HashMap<Uuid, UserRow>,
    username: HashMap<String, Uuid>,
    sessions: HashMap<Uuid, SessionRow>,
    refresh: HashMap<[u8; 32], Uuid>,
    servers: HashMap<Uuid, ServerRow>,
    members: HashSet<(Uuid, Uuid)>,
    roles: HashMap<Uuid, RoleRow>,
    member_roles: HashSet<(Uuid, Uuid, Uuid)>,
    channels: HashMap<Uuid, ChannelRow>,
    invites: HashMap<String, (Uuid, Uuid)>,
    messages: HashMap<Uuid, MessageRow>,
    by_channel: HashMap<Uuid, BTreeMap<Uuid, Uuid>>,
    attachments: HashMap<Uuid, AttachmentRow>,
    outbox: Vec<OutboxRow>,
    outbox_seq: i64,
    gateway: HashMap<Uuid, GwRow>,
    idempotency: HashMap<(Uuid, String), Uuid>,
    path: PathBuf,
}

impl Inner {
    fn load(path: PathBuf) -> Self {
        let snap: Snapshot = std::fs::read(&path)
            .ok()
            .and_then(|bytes| serde_json::from_slice(&bytes).ok())
            .unwrap_or_default();
        let mut inner = Self {
            users: HashMap::new(),
            username: HashMap::new(),
            sessions: HashMap::new(),
            refresh: HashMap::new(),
            servers: HashMap::new(),
            members: snap.members.into_iter().collect(),
            roles: snap.roles.into_iter().map(|r| (r.id, r)).collect(),
            member_roles: snap.member_roles.into_iter().collect(),
            channels: HashMap::new(),
            invites: snap
                .invites
                .into_iter()
                .map(|(c, s, u)| (c, (s, u)))
                .collect(),
            messages: HashMap::new(),
            by_channel: HashMap::new(),
            attachments: HashMap::new(),
            outbox: snap.outbox,
            outbox_seq: snap.outbox_seq,
            gateway: snap.gateway.into_iter().map(|g| (g.id, g)).collect(),
            idempotency: snap
                .idempotency
                .into_iter()
                .map(|(u, k, m)| ((u, k), m))
                .collect(),
            path,
        };
        for user in snap.users {
            inner.username.insert(user.username.clone(), user.id);
            inner.users.insert(user.id, user);
        }
        for session in snap.sessions {
            inner.refresh.insert(session.refresh_hash, session.id);
            inner.sessions.insert(session.id, session);
        }
        for server in snap.servers {
            inner.servers.insert(server.id, server);
        }
        for channel in snap.channels {
            inner.channels.insert(channel.id, channel);
        }
        for message in snap.messages {
            inner
                .by_channel
                .entry(message.channel_id)
                .or_default()
                .insert(message.id, message.id);
            inner.messages.insert(message.id, message);
        }
        for attachment in snap.attachments {
            inner
                .attachments
                .insert(attachment.attachment.id, attachment);
        }
        inner
    }

    fn persist(&self) {
        if let Some(parent) = self.path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }
        let snap = Snapshot {
            users: self.users.values().cloned().collect(),
            sessions: self.sessions.values().cloned().collect(),
            servers: self.servers.values().cloned().collect(),
            members: self.members.iter().copied().collect(),
            roles: self.roles.values().cloned().collect(),
            member_roles: self.member_roles.iter().copied().collect(),
            channels: self.channels.values().cloned().collect(),
            invites: self
                .invites
                .iter()
                .map(|(c, (s, u))| (c.clone(), *s, *u))
                .collect(),
            messages: self.messages.values().cloned().collect(),
            attachments: self.attachments.values().cloned().collect(),
            outbox: self.outbox.clone(),
            outbox_seq: self.outbox_seq,
            gateway: self.gateway.values().cloned().collect(),
            idempotency: self
                .idempotency
                .iter()
                .map(|((u, k), m)| (*u, k.clone(), *m))
                .collect(),
        };
        if let Ok(bytes) = serde_json::to_vec(&snap) {
            let tmp = self.path.with_extension("json.tmp");
            if std::fs::write(&tmp, bytes).is_ok() {
                let _ = std::fs::rename(tmp, &self.path);
            }
        }
    }

    fn user(&self, id: Uuid) -> Option<User> {
        let u = self.users.get(&id)?;
        let avatar = u.avatar_id.and_then(|aid| {
            self.attachments
                .get(&aid)
                .map(|row| to_attachment(&row.attachment))
        });
        Some(User {
            id: u.id,
            username: u.username.clone(),
            display_name: u.display_name.clone(),
            avatar_animated: u.avatar_animated && avatar.is_some(),
            avatar,
        })
    }

    fn server(&self, id: Uuid) -> Option<Server> {
        self.servers.get(&id).map(|s| Server {
            id: s.id,
            name: s.name.clone(),
            owner_id: s.owner_id,
            invite_code: s.invite_code.clone(),
            blocked_words: s.blocked_words.clone(),
            cooldown_seconds: s.cooldown_seconds,
        })
    }

    fn channel(&self, id: Uuid) -> Option<Channel> {
        self.channels.get(&id).and_then(|c| {
            Some(Channel {
                id: c.id,
                server_id: c.server_id,
                name: c.name.clone(),
                kind: ChannelKind::parse(&c.kind)?,
                audio_quality: chat_domain::voice::AudioQuality::parse(&c.audio_quality)
                    .unwrap_or(chat_domain::voice::AudioQuality::DEFAULT),
            })
        })
    }

    fn message(&self, id: Uuid) -> Option<Message> {
        let row = self.messages.get(&id)?;
        Some(Message {
            id: row.id,
            channel_id: row.channel_id,
            author_id: row.author_id,
            kind: row.kind.clone(),
            content: row.content.clone(),
            created_at: row.created_at.clone(),
            edited_at: row.edited_at.clone(),
            reply_to: row.reply_to,
            mentions: row.mentions.clone(),
            attachments: self
                .attachments
                .values()
                .filter(|a| a.message_id == Some(id))
                .map(|a| to_attachment(&a.attachment))
                .collect(),
        })
    }

    fn enqueue(&mut self, members: &[Uuid], event: &str, payload: Value) -> Vec<OutboxEvent> {
        let mut events = Vec::with_capacity(members.len());
        let now = Utc::now().timestamp();
        for user_id in members {
            self.outbox_seq += 1;
            let row = OutboxRow {
                seq: self.outbox_seq,
                user_id: *user_id,
                event: event.into(),
                payload: payload.clone(),
                created_at: now,
            };
            events.push(OutboxEvent {
                seq: row.seq,
                user_id: row.user_id,
                event: row.event.clone(),
                payload: row.payload.clone(),
            });
            self.outbox.push(row);
        }
        events
    }
}

fn to_attachment(dto: &AttachmentDto) -> Attachment {
    Attachment {
        id: dto.id,
        file_name: dto.file_name.clone(),
        mime_type: dto.mime_type.clone(),
        size: dto.size,
        object_key: dto.object_key,
        thumbnail_key: dto.thumbnail_key,
    }
}

pub struct LocalStore(Mutex<Inner>);

impl LocalStore {
    pub fn open(url: &str) -> Result<Self, StoreError> {
        let path = PathBuf::from(url.trim_start_matches("local:"));
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent).map_err(|_| StoreError::Unavailable)?;
        }
        Ok(Self(Mutex::new(Inner::load(path))))
    }
}

#[async_trait]
impl Store for LocalStore {
    async fn ping(&self) -> Result<(), StoreError> {
        Ok(())
    }

    async fn create_user(
        &self,
        id: Uuid,
        username: &str,
        display_name: &str,
        password_hash: &str,
    ) -> Result<User, StoreError> {
        let mut inner = self.0.lock().await;
        if inner.username.contains_key(username) {
            return Err(StoreError::Conflict("Username already taken.".into()));
        }
        inner.users.insert(
            id,
            UserRow {
                id,
                username: username.into(),
                display_name: display_name.into(),
                password_hash: password_hash.into(),
                avatar_id: None,
                avatar_animated: false,
            },
        );
        inner.username.insert(username.into(), id);
        inner.persist();
        Ok(User {
            id,
            username: username.into(),
            display_name: display_name.into(),
            avatar: None,
            avatar_animated: false,
        })
    }

    async fn find_user(&self, id: Uuid) -> Result<Option<User>, StoreError> {
        Ok(self.0.lock().await.user(id))
    }

    async fn find_user_by_username(
        &self,
        username: &str,
    ) -> Result<Option<(User, String)>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner.username.get(username).and_then(|id| {
            let hash = inner.users.get(id)?.password_hash.clone();
            Some((inner.user(*id)?, hash))
        }))
    }

    async fn update_user(
        &self,
        id: Uuid,
        username: &str,
        display_name: &str,
        avatar_id: Option<Uuid>,
        avatar_animated: bool,
    ) -> Result<User, StoreError> {
        let mut inner = self.0.lock().await;
        if inner
            .username
            .get(username)
            .is_some_and(|other| *other != id)
        {
            return Err(StoreError::Conflict("Username already taken.".into()));
        }
        let old_name = inner
            .users
            .get(&id)
            .ok_or(StoreError::NotFound)?
            .username
            .clone();
        if old_name != username {
            inner.username.remove(&old_name);
            inner.username.insert(username.into(), id);
        }
        {
            let row = inner.users.get_mut(&id).ok_or(StoreError::NotFound)?;
            row.username = username.into();
            row.display_name = display_name.into();
            row.avatar_id = avatar_id;
            row.avatar_animated = avatar_animated;
        }
        let user = inner.user(id).ok_or(StoreError::Unavailable)?;
        inner.persist();
        Ok(user)
    }

    async fn avatar_owner(&self, attachment_id: Uuid) -> Result<Option<Uuid>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner
            .users
            .values()
            .find(|row| row.avatar_id == Some(attachment_id))
            .map(|row| row.id))
    }

    async fn shares_community(&self, a: Uuid, b: Uuid) -> Result<bool, StoreError> {
        if a == b {
            return Ok(true);
        }
        let inner = self.0.lock().await;
        let servers: HashSet<Uuid> = inner
            .members
            .iter()
            .filter(|(_, user)| *user == a)
            .map(|(server, _)| *server)
            .collect();
        Ok(inner
            .members
            .iter()
            .any(|(server, user)| *user == b && servers.contains(server)))
    }

    async fn create_session(&self, session: SessionRecord) -> Result<(), StoreError> {
        let mut inner = self.0.lock().await;
        inner.refresh.insert(session.refresh_hash, session.id);
        inner.sessions.insert(
            session.id,
            SessionRow {
                id: session.id,
                user_id: session.user_id,
                refresh_hash: session.refresh_hash,
                expires_at: session.expires_at,
                revoked: session.revoked,
            },
        );
        inner.persist();
        Ok(())
    }

    async fn find_session_by_refresh(
        &self,
        hash: [u8; 32],
    ) -> Result<Option<SessionRecord>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner.refresh.get(&hash).and_then(|id| {
            inner.sessions.get(id).map(|s| SessionRecord {
                id: s.id,
                user_id: s.user_id,
                refresh_hash: s.refresh_hash,
                expires_at: s.expires_at,
                revoked: s.revoked,
            })
        }))
    }

    async fn rotate_session(
        &self,
        id: Uuid,
        hash: [u8; 32],
        expires_at: i64,
    ) -> Result<(), StoreError> {
        let mut inner = self.0.lock().await;
        let old = inner
            .sessions
            .get(&id)
            .map(|session| session.refresh_hash)
            .ok_or(StoreError::NotFound)?;
        inner.refresh.remove(&old);
        let session = inner.sessions.get_mut(&id).ok_or(StoreError::NotFound)?;
        session.refresh_hash = hash;
        session.expires_at = expires_at;
        session.revoked = false;
        inner.refresh.insert(hash, id);
        inner.persist();
        Ok(())
    }

    async fn revoke_session(&self, id: Uuid) -> Result<(), StoreError> {
        let mut inner = self.0.lock().await;
        let hash = inner.sessions.get_mut(&id).map(|session| {
            session.revoked = true;
            session.refresh_hash
        });
        if let Some(hash) = hash {
            inner.refresh.remove(&hash);
            inner.persist();
        }
        Ok(())
    }

    async fn create_server(
        &self,
        owner: Uuid,
        server: Server,
        base_role: Uuid,
        owner_role: Uuid,
        channel: Channel,
    ) -> Result<NewServer, StoreError> {
        let mut inner = self.0.lock().await;
        inner.servers.insert(
            server.id,
            ServerRow {
                id: server.id,
                name: server.name.clone(),
                owner_id: owner,
                invite_code: server.invite_code.clone(),
                blocked_words: server.blocked_words.clone(),
                cooldown_seconds: server.cooldown_seconds,
            },
        );
        inner.members.insert((server.id, owner));
        inner.roles.insert(
            base_role,
            RoleRow {
                id: base_role,
                server_id: server.id,
                permissions: Permissions::VIEW_CHANNEL.0
                    | Permissions::SEND_MESSAGE.0
                    | Permissions::CONNECT_VOICE.0
                    | Permissions::SPEAK.0,
                is_base: true,
            },
        );
        inner.roles.insert(
            owner_role,
            RoleRow {
                id: owner_role,
                server_id: server.id,
                permissions: Permissions::ADMINISTRATOR.0,
                is_base: false,
            },
        );
        inner.member_roles.insert((server.id, owner, base_role));
        inner.member_roles.insert((server.id, owner, owner_role));
        inner.channels.insert(
            channel.id,
            ChannelRow {
                id: channel.id,
                server_id: server.id,
                name: channel.name.clone(),
                kind: channel.kind.as_str().into(),
                audio_quality: channel.audio_quality.as_str().into(),
            },
        );
        inner
            .invites
            .insert(server.invite_code.clone(), (server.id, owner));
        inner.persist();
        Ok(NewServer { server, channel })
    }

    async fn list_servers(&self, user: Uuid) -> Result<Vec<Server>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner
            .members
            .iter()
            .filter(|(_, u)| *u == user)
            .filter_map(|(s, _)| inner.server(*s))
            .collect())
    }

    async fn list_channels(&self, server: Uuid) -> Result<Vec<Channel>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner
            .channels
            .values()
            .filter(|c| c.server_id == server)
            .filter_map(|c| inner.channel(c.id))
            .collect())
    }

    async fn find_channel(&self, id: Uuid) -> Result<Option<Channel>, StoreError> {
        Ok(self.0.lock().await.channel(id))
    }

    async fn find_server(&self, id: Uuid) -> Result<Option<Server>, StoreError> {
        Ok(self.0.lock().await.server(id))
    }

    async fn update_moderation(
        &self,
        id: Uuid,
        blocked_words: Vec<String>,
        cooldown_seconds: u32,
    ) -> Result<Server, StoreError> {
        let mut inner = self.0.lock().await;
        let row = inner.servers.get_mut(&id).ok_or(StoreError::NotFound)?;
        row.blocked_words = blocked_words;
        row.cooldown_seconds = cooldown_seconds;
        let server = inner.server(id).ok_or(StoreError::NotFound)?;
        inner.persist();
        Ok(server)
    }

    async fn last_user_message_at(
        &self,
        channel: Uuid,
        user: Uuid,
    ) -> Result<Option<i64>, StoreError> {
        let inner = self.0.lock().await;
        let Some(ids) = inner.by_channel.get(&channel) else {
            return Ok(None);
        };
        for id in ids.keys().rev() {
            if let Some(message) = inner.messages.get(id)
                && message.author_id == user
            {
                let ts = chrono::DateTime::parse_from_rfc3339(&message.created_at)
                    .map(|value| value.timestamp())
                    .unwrap_or(0);
                return Ok(Some(ts));
            }
        }
        Ok(None)
    }

    async fn add_channel(&self, channel: Channel) -> Result<Channel, StoreError> {
        let mut inner = self.0.lock().await;
        inner.channels.insert(
            channel.id,
            ChannelRow {
                id: channel.id,
                server_id: channel.server_id,
                name: channel.name.clone(),
                kind: channel.kind.as_str().into(),
                audio_quality: channel.audio_quality.as_str().into(),
            },
        );
        inner.persist();
        Ok(channel)
    }

    async fn set_channel_audio_quality(
        &self,
        id: Uuid,
        quality: chat_domain::voice::AudioQuality,
    ) -> Result<Channel, StoreError> {
        let mut inner = self.0.lock().await;
        {
            let row = inner.channels.get_mut(&id).ok_or(StoreError::NotFound)?;
            row.audio_quality = quality.as_str().into();
        }
        let channel = inner.channel(id).ok_or(StoreError::NotFound)?;
        inner.persist();
        Ok(channel)
    }

    async fn join_invite(&self, user: Uuid, code: &str) -> Result<Server, StoreError> {
        let mut inner = self.0.lock().await;
        let (server_id, _) = inner
            .invites
            .get(code)
            .copied()
            .ok_or(StoreError::NotFound)?;
        if inner.members.contains(&(server_id, user)) {
            return inner.server(server_id).ok_or(StoreError::NotFound);
        }
        inner.members.insert((server_id, user));
        if let Some(base) = inner
            .roles
            .values()
            .find(|r| r.server_id == server_id && r.is_base)
            .map(|r| r.id)
        {
            inner.member_roles.insert((server_id, user, base));
        }
        let server = inner.server(server_id).ok_or(StoreError::NotFound)?;
        inner.persist();
        Ok(server)
    }

    async fn list_members(&self, server: Uuid) -> Result<Vec<Uuid>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner
            .members
            .iter()
            .filter(|(s, _)| *s == server)
            .map(|(_, u)| *u)
            .collect())
    }

    async fn list_visible_users(&self, user: Uuid) -> Result<Vec<User>, StoreError> {
        let inner = self.0.lock().await;
        let servers: HashSet<Uuid> = inner
            .members
            .iter()
            .filter(|(_, u)| *u == user)
            .map(|(s, _)| *s)
            .collect();
        let mut users = HashMap::new();
        for (server, member) in &inner.members {
            if servers.contains(server)
                && let Some(u) = inner.user(*member)
            {
                users.insert(u.id, u);
            }
        }
        Ok(users.into_values().collect())
    }

    async fn permissions(
        &self,
        user: Uuid,
        channel: Uuid,
    ) -> Result<PermissionSnapshot, StoreError> {
        let inner = self.0.lock().await;
        let channel = inner.channel(channel).ok_or(StoreError::NotFound)?;
        let member = inner.members.contains(&(channel.server_id, user));
        let base = inner
            .roles
            .values()
            .find(|r| r.server_id == channel.server_id && r.is_base)
            .map(|r| Permissions(r.permissions))
            .unwrap_or(Permissions(0));
        let roles = inner
            .member_roles
            .iter()
            .filter(|(s, u, _)| *s == channel.server_id && *u == user)
            .filter_map(|(_, _, role)| inner.roles.get(role).map(|r| Permissions(r.permissions)))
            .collect();
        Ok(PermissionSnapshot {
            member,
            base,
            roles,
            everyone: (Permissions(0), Permissions(0)),
            role_overrides: vec![],
            member_override: (Permissions(0), Permissions(0)),
        })
    }

    async fn insert_attachment(
        &self,
        attachment: Attachment,
        uploader: Uuid,
    ) -> Result<(), StoreError> {
        let mut inner = self.0.lock().await;
        inner.attachments.insert(
            attachment.id,
            AttachmentRow {
                attachment: AttachmentDto {
                    id: attachment.id,
                    file_name: attachment.file_name,
                    mime_type: attachment.mime_type,
                    size: attachment.size,
                    object_key: attachment.object_key,
                    thumbnail_key: attachment.thumbnail_key,
                },
                uploader_id: uploader,
                message_id: None,
            },
        );
        inner.persist();
        Ok(())
    }

    async fn get_attachment(
        &self,
        id: Uuid,
    ) -> Result<Option<(Attachment, Uuid, Option<Uuid>)>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner.attachments.get(&id).map(|row| {
            (
                to_attachment(&row.attachment),
                row.uploader_id,
                row.message_id,
            )
        }))
    }

    async fn message_channel(&self, message_id: Uuid) -> Result<Option<Uuid>, StoreError> {
        Ok(self
            .0
            .lock()
            .await
            .messages
            .get(&message_id)
            .map(|row| row.channel_id))
    }

    async fn bind_attachments(
        &self,
        message_id: Uuid,
        uploader: Uuid,
        ids: &[Uuid],
    ) -> Result<Vec<Attachment>, StoreError> {
        let mut inner = self.0.lock().await;
        let mut bound = Vec::new();
        for id in ids {
            let in_use = inner.users.values().any(|user| user.avatar_id == Some(*id));
            let row = inner.attachments.get_mut(id).ok_or(StoreError::NotFound)?;
            if row.uploader_id != uploader || row.message_id.is_some() || in_use {
                return Err(StoreError::Conflict("Attachment already used.".into()));
            }
            row.message_id = Some(message_id);
            bound.push(to_attachment(&row.attachment));
        }
        inner.persist();
        Ok(bound)
    }

    async fn find_idempotent(&self, user: Uuid, key: &str) -> Result<Option<Message>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner
            .idempotency
            .get(&(user, key.into()))
            .and_then(|id| inner.message(*id)))
    }

    async fn get_message(&self, id: Uuid) -> Result<Option<Message>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner.message(id))
    }

    async fn insert_message(
        &self,
        mut message: Message,
        member_ids: &[Uuid],
        event: &str,
        payload: Value,
        idempotency_key: Option<&str>,
    ) -> Result<(Message, Vec<OutboxEvent>), StoreError> {
        let mut inner = self.0.lock().await;
        inner.messages.insert(
            message.id,
            MessageRow {
                id: message.id,
                channel_id: message.channel_id,
                author_id: message.author_id,
                kind: message.kind.clone(),
                content: message.content.clone(),
                created_at: message.created_at.clone(),
                edited_at: message.edited_at.clone(),
                reply_to: message.reply_to,
                mentions: message.mentions.clone(),
            },
        );
        inner
            .by_channel
            .entry(message.channel_id)
            .or_default()
            .insert(message.id, message.id);
        if let Some(key) = idempotency_key {
            inner
                .idempotency
                .insert((message.author_id, key.into()), message.id);
        }
        let events = inner.enqueue(member_ids, event, payload);
        message.attachments = inner
            .attachments
            .values()
            .filter(|a| a.message_id == Some(message.id))
            .map(|a| to_attachment(&a.attachment))
            .collect();
        inner.persist();
        Ok((message, events))
    }

    async fn update_message(
        &self,
        mut message: Message,
        member_ids: &[Uuid],
        event: &str,
        payload: Value,
    ) -> Result<(Message, Vec<OutboxEvent>), StoreError> {
        let mut inner = self.0.lock().await;
        {
            let row = inner
                .messages
                .get_mut(&message.id)
                .ok_or(StoreError::NotFound)?;
            row.content = message.content.clone();
            row.edited_at = message.edited_at.clone();
            row.mentions = message.mentions.clone();
        }
        let events = inner.enqueue(member_ids, event, payload);
        message.attachments = inner
            .attachments
            .values()
            .filter(|a| a.message_id == Some(message.id))
            .map(|a| to_attachment(&a.attachment))
            .collect();
        inner.persist();
        Ok((message, events))
    }

    async fn page_messages(
        &self,
        channel: Uuid,
        before: Option<Uuid>,
        limit: u32,
    ) -> Result<(Vec<Message>, Option<Uuid>), StoreError> {
        let inner = self.0.lock().await;
        let empty = BTreeMap::new();
        let ids = inner.by_channel.get(&channel).unwrap_or(&empty);
        let iter = ids.keys().rev().copied();
        let filtered: Vec<Uuid> = match before {
            Some(before) => iter
                .filter(|id| *id < before)
                .take(limit as usize + 1)
                .collect(),
            None => iter.take(limit as usize + 1).collect(),
        };
        let has_more = filtered.len() > limit as usize;
        let page: Vec<Uuid> = filtered.into_iter().take(limit as usize).collect();
        let messages: Vec<Message> = page.iter().filter_map(|id| inner.message(*id)).collect();
        let next = if has_more {
            messages.last().map(|m| m.id)
        } else {
            None
        };
        Ok((messages, next))
    }

    async fn enqueue(
        &self,
        member_ids: &[Uuid],
        event: &str,
        payload: Value,
    ) -> Result<Vec<OutboxEvent>, StoreError> {
        let mut inner = self.0.lock().await;
        let events = inner.enqueue(member_ids, event, payload);
        inner.persist();
        Ok(events)
    }

    async fn create_gateway_session(
        &self,
        id: Uuid,
        user: Uuid,
        expires_at: i64,
    ) -> Result<GatewaySession, StoreError> {
        let mut inner = self.0.lock().await;
        let row = GwRow {
            id,
            user_id: user,
            last_seq: 0,
            expires_at,
        };
        inner.gateway.insert(id, row.clone());
        inner.persist();
        Ok(GatewaySession {
            id: row.id,
            user_id: row.user_id,
            last_seq: row.last_seq,
            expires_at: row.expires_at,
        })
    }

    async fn find_gateway_session(&self, id: Uuid) -> Result<Option<GatewaySession>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner.gateway.get(&id).map(|g| GatewaySession {
            id: g.id,
            user_id: g.user_id,
            last_seq: g.last_seq,
            expires_at: g.expires_at,
        }))
    }

    async fn touch_gateway_session(
        &self,
        id: Uuid,
        last_seq: i64,
        expires_at: i64,
    ) -> Result<(), StoreError> {
        let mut inner = self.0.lock().await;
        if let Some(row) = inner.gateway.get_mut(&id) {
            row.last_seq = last_seq;
            row.expires_at = expires_at;
            inner.persist();
        }
        Ok(())
    }

    async fn replay(
        &self,
        user: Uuid,
        after: i64,
        limit: u32,
    ) -> Result<Vec<OutboxEvent>, StoreError> {
        let inner = self.0.lock().await;
        Ok(inner
            .outbox
            .iter()
            .filter(|row| row.user_id == user && row.seq > after)
            .take(limit as usize)
            .map(|row| OutboxEvent {
                seq: row.seq,
                user_id: row.user_id,
                event: row.event.clone(),
                payload: row.payload.clone(),
            })
            .collect())
    }

    async fn trim_outbox(&self, retain: i64, max_per_user: i64) -> Result<(), StoreError> {
        let mut inner = self.0.lock().await;
        let cutoff = Utc::now().timestamp() - retain;
        inner.outbox.retain(|row| row.created_at >= cutoff);
        let mut counts: HashMap<Uuid, i64> = HashMap::new();
        inner.outbox.sort_by_key(|row| row.seq);
        inner.outbox.retain(|row| {
            let n = counts.entry(row.user_id).or_insert(0);
            *n += 1;
            *n <= max_per_user
        });
        inner.persist();
        Ok(())
    }
}
