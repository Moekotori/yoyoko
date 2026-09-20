use crate::error::ApiErr;
use async_trait::async_trait;
use chat_domain::{
    channel::{Channel, Server},
    message::{Attachment, Message},
    permission::Permissions,
    user::User,
};
use serde_json::Value;
use uuid::Uuid;

#[derive(Debug)]
pub enum StoreError {
    Conflict(String),
    NotFound,
    Unavailable,
}

impl std::fmt::Display for StoreError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Conflict(message) => write!(f, "{message}"),
            Self::NotFound => write!(f, "not found"),
            Self::Unavailable => write!(f, "unavailable"),
        }
    }
}
impl std::error::Error for StoreError {}

impl From<StoreError> for ApiErr {
    fn from(value: StoreError) -> Self {
        match value {
            StoreError::Conflict(message) => ApiErr::conflict(message),
            StoreError::NotFound => ApiErr::not_found(),
            StoreError::Unavailable => ApiErr::unavailable(),
        }
    }
}

#[derive(Debug, Clone)]
pub struct SessionRecord {
    pub id: Uuid,
    pub user_id: Uuid,
    pub refresh_hash: [u8; 32],
    pub expires_at: i64,
    pub revoked: bool,
}

#[derive(Debug, Clone)]
pub struct GatewaySession {
    pub id: Uuid,
    pub user_id: Uuid,
    pub last_seq: i64,
    pub expires_at: i64,
}

#[derive(Debug, Clone)]
pub struct OutboxEvent {
    pub seq: i64,
    pub user_id: Uuid,
    pub event: String,
    pub payload: Value,
}

#[derive(Debug, Clone)]
pub struct PermissionSnapshot {
    pub member: bool,
    pub base: Permissions,
    pub roles: Vec<Permissions>,
    pub everyone: (Permissions, Permissions),
    pub role_overrides: Vec<(Permissions, Permissions)>,
    pub member_override: (Permissions, Permissions),
}

impl PermissionSnapshot {
    pub fn resolve(&self) -> Option<Permissions> {
        if !self.member {
            return None;
        }
        Some(Permissions::resolve(
            self.base,
            &self.roles,
            chat_domain::permission::Override {
                allow: self.everyone.0,
                deny: self.everyone.1,
            },
            &self
                .role_overrides
                .iter()
                .map(|(allow, deny)| chat_domain::permission::Override {
                    allow: *allow,
                    deny: *deny,
                })
                .collect::<Vec<_>>(),
            chat_domain::permission::Override {
                allow: self.member_override.0,
                deny: self.member_override.1,
            },
        ))
    }
}

#[derive(Debug, Clone)]
pub struct NewServer {
    pub server: Server,
    pub channel: Channel,
}

#[async_trait]
pub trait Store: Send + Sync {
    async fn ping(&self) -> Result<(), StoreError>;

    async fn create_user(
        &self,
        id: Uuid,
        username: &str,
        display_name: &str,
        password_hash: &str,
    ) -> Result<User, StoreError>;
    async fn find_user(&self, id: Uuid) -> Result<Option<User>, StoreError>;
    async fn find_user_by_username(
        &self,
        username: &str,
    ) -> Result<Option<(User, String)>, StoreError>;
    async fn update_user(
        &self,
        id: Uuid,
        username: &str,
        display_name: &str,
        avatar_id: Option<Uuid>,
        avatar_animated: bool,
    ) -> Result<User, StoreError>;
    async fn avatar_owner(&self, attachment_id: Uuid) -> Result<Option<Uuid>, StoreError>;
    async fn shares_community(&self, a: Uuid, b: Uuid) -> Result<bool, StoreError>;

    async fn create_session(&self, session: SessionRecord) -> Result<(), StoreError>;
    async fn find_session_by_refresh(
        &self,
        hash: [u8; 32],
    ) -> Result<Option<SessionRecord>, StoreError>;
    async fn rotate_session(
        &self,
        id: Uuid,
        hash: [u8; 32],
        expires_at: i64,
    ) -> Result<(), StoreError>;
    async fn revoke_session(&self, id: Uuid) -> Result<(), StoreError>;

    async fn create_server(
        &self,
        owner: Uuid,
        server: Server,
        base_role: Uuid,
        owner_role: Uuid,
        channel: Channel,
    ) -> Result<NewServer, StoreError>;
    async fn list_servers(&self, user: Uuid) -> Result<Vec<Server>, StoreError>;
    async fn list_channels(&self, server: Uuid) -> Result<Vec<Channel>, StoreError>;
    async fn find_channel(&self, id: Uuid) -> Result<Option<Channel>, StoreError>;
    async fn find_server(&self, id: Uuid) -> Result<Option<Server>, StoreError>;
    async fn update_moderation(
        &self,
        id: Uuid,
        blocked_words: Vec<String>,
        cooldown_seconds: u32,
    ) -> Result<Server, StoreError>;
    async fn last_user_message_at(
        &self,
        channel: Uuid,
        user: Uuid,
    ) -> Result<Option<i64>, StoreError>;
    async fn add_channel(&self, channel: Channel) -> Result<Channel, StoreError>;
    async fn set_channel_audio_quality(
        &self,
        id: Uuid,
        quality: chat_domain::voice::AudioQuality,
    ) -> Result<Channel, StoreError>;
    async fn join_invite(&self, user: Uuid, code: &str) -> Result<Server, StoreError>;
    async fn list_members(&self, server: Uuid) -> Result<Vec<Uuid>, StoreError>;
    async fn list_visible_users(&self, user: Uuid) -> Result<Vec<User>, StoreError>;
    async fn permissions(
        &self,
        user: Uuid,
        channel: Uuid,
    ) -> Result<PermissionSnapshot, StoreError>;

    async fn insert_attachment(
        &self,
        attachment: Attachment,
        uploader: Uuid,
    ) -> Result<(), StoreError>;
    async fn get_attachment(
        &self,
        id: Uuid,
    ) -> Result<Option<(Attachment, Uuid, Option<Uuid>)>, StoreError>;
    async fn message_channel(&self, message_id: Uuid) -> Result<Option<Uuid>, StoreError>;
    async fn bind_attachments(
        &self,
        message_id: Uuid,
        uploader: Uuid,
        ids: &[Uuid],
    ) -> Result<Vec<Attachment>, StoreError>;

    async fn find_idempotent(&self, user: Uuid, key: &str) -> Result<Option<Message>, StoreError>;
    async fn insert_message(
        &self,
        message: Message,
        member_ids: &[Uuid],
        event: &str,
        payload: Value,
        idempotency_key: Option<&str>,
    ) -> Result<(Message, Vec<OutboxEvent>), StoreError>;
    async fn page_messages(
        &self,
        channel: Uuid,
        before: Option<Uuid>,
        limit: u32,
    ) -> Result<(Vec<Message>, Option<Uuid>), StoreError>;

    async fn enqueue(
        &self,
        member_ids: &[Uuid],
        event: &str,
        payload: Value,
    ) -> Result<Vec<OutboxEvent>, StoreError>;
    async fn create_gateway_session(
        &self,
        id: Uuid,
        user: Uuid,
        expires_at: i64,
    ) -> Result<GatewaySession, StoreError>;
    async fn find_gateway_session(&self, id: Uuid) -> Result<Option<GatewaySession>, StoreError>;
    async fn touch_gateway_session(
        &self,
        id: Uuid,
        last_seq: i64,
        expires_at: i64,
    ) -> Result<(), StoreError>;
    async fn replay(
        &self,
        user: Uuid,
        after: i64,
        limit: u32,
    ) -> Result<Vec<OutboxEvent>, StoreError>;
    async fn trim_outbox(&self, retain: i64, max_per_user: i64) -> Result<(), StoreError>;
}
