use serde::{Deserialize, Serialize};
use serde_json::Value;
use uuid::Uuid;

pub const PROTOCOL_VERSION: u32 = 1;
pub const API_VERSION: u32 = 1;
pub const MAX_GATEWAY_BYTES: usize = 65_536;
pub const MAX_PAGE_SIZE: u32 = 100;
pub const DEFAULT_PAGE_SIZE: u32 = 50;
pub const MAX_CONTENT_BYTES: usize = 16_384;
pub const MAX_ATTACHMENT_BYTES: u64 = 25_165_824;
pub const MAX_ATTACHMENT_BYTES_CEILING: u64 = 268_435_456;
pub const MAX_ATTACHMENTS_PER_MESSAGE: usize = 4;
pub const MAX_AVATAR_BYTES: u64 = 8_388_608;
pub const MAX_AVATAR_EDGE: u32 = 4_096;
fn default_max_attachment_bytes() -> u64 {
    MAX_ATTACHMENT_BYTES
}
fn default_max_attachments_per_message() -> u32 {
    MAX_ATTACHMENTS_PER_MESSAGE as u32
}

#[derive(Debug, Serialize, Deserialize)]
pub struct InstanceDiscovery {
    pub instance_id: Uuid,
    pub name: String,
    pub protocol_version: u32,
    pub api_version: u32,
    pub api: String,
    pub gateway: String,
    pub cdn: String,
    pub rtc: String,
    #[serde(default = "default_max_attachment_bytes")]
    pub max_attachment_bytes: u64,
    #[serde(default = "default_max_attachments_per_message")]
    pub max_attachments_per_message: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GatewayEnvelope {
    pub op: String,
    pub event: Option<String>,
    pub seq: Option<String>,
    pub data: Value,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Identify {
    pub protocol_version: u32,
    pub access_token: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Resume {
    pub protocol_version: u32,
    pub access_token: String,
    pub session_id: String,
    pub last_seq: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct ApiError {
    pub code: String,
    pub message: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub retry_after_seconds: Option<u32>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct User {
    pub id: Uuid,
    pub username: String,
    pub display_name: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub avatar: Option<Avatar>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub banner: Option<Avatar>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Avatar {
    pub id: Uuid,
    pub mime_type: String,
    pub size: u64,
    pub download_url: String,
    pub thumbnail_url: Option<String>,
    #[serde(default)]
    pub animated: bool,
}

#[derive(Debug, Serialize, Deserialize, Default)]
pub struct PatchMeRequest {
    pub username: Option<String>,
    pub display_name: Option<String>,
    pub avatar_id: Option<Uuid>,
    #[serde(default)]
    pub clear_avatar: bool,
    pub banner_id: Option<Uuid>,
    #[serde(default)]
    pub clear_banner: bool,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct RegisterRequest {
    pub username: String,
    pub display_name: String,
    pub password: String,
    #[serde(default)]
    pub server_password: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct LoginRequest {
    pub username: String,
    pub password: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct RefreshRequest {
    pub refresh_token: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct AuthResponse {
    pub access_token: String,
    pub refresh_token: String,
    pub expires_in: u64,
    pub user: User,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Server {
    pub id: Uuid,
    pub name: String,
    pub owner_id: Uuid,
    pub invite_code: String,
    #[serde(default)]
    pub blocked_words: Vec<String>,
    #[serde(default)]
    pub cooldown_seconds: u32,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct PatchModerationRequest {
    #[serde(default)]
    pub blocked_words: Option<Vec<String>>,
    #[serde(default)]
    pub cooldown_seconds: Option<u32>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Channel {
    pub id: Uuid,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub server_id: Option<Uuid>,
    pub name: String,
    pub kind: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub audio_quality: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub participants: Option<Vec<Uuid>>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct OpenDmRequest {
    pub recipient_id: Uuid,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct CreateServerRequest {
    pub name: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct CreateChannelRequest {
    pub name: String,
    pub kind: String,
    #[serde(default)]
    pub audio_quality: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct PatchChannelRequest {
    #[serde(default)]
    pub name: Option<String>,
    #[serde(default)]
    pub audio_quality: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct JoinRequest {
    pub invite_code: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct SendMessageRequest {
    pub content: Option<String>,
    pub reply_to: Option<Uuid>,
    #[serde(default)]
    pub attachment_ids: Vec<Uuid>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct PatchMessageRequest {
    pub content: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Attachment {
    pub id: Uuid,
    pub file_name: String,
    pub mime_type: String,
    pub size: u64,
    pub download_url: String,
    pub thumbnail_url: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Message {
    pub id: Uuid,
    pub channel_id: Uuid,
    pub author_id: Uuid,
    pub kind: String,
    pub content: Option<String>,
    pub created_at: String,
    pub edited_at: Option<String>,
    pub reply_to: Option<Uuid>,
    pub mentions: Vec<Uuid>,
    #[serde(default)]
    pub mention_everyone: bool,
    #[serde(default)]
    pub mention_here: bool,
    pub attachments: Vec<Attachment>,
    pub embeds: Vec<Value>,
    pub reactions: Vec<Value>,
    pub encrypted_payload: Option<Value>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MessageDelete {
    pub id: Uuid,
    pub channel_id: Uuid,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Typing {
    pub user_id: Uuid,
    pub channel_id: Uuid,
    pub display_name: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct MessagePage {
    pub items: Vec<Message>,
    pub before: Option<Uuid>,
}

#[derive(Debug, Serialize, Deserialize, Default)]
pub struct VoiceFlags {
    #[serde(default)]
    pub self_mute: bool,
    #[serde(default)]
    pub self_deaf: bool,
    #[serde(default)]
    pub audio_quality: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AudioProfile {
    pub id: String,
    pub sample_rate_hz: u32,
    pub channels: u8,
    pub bitrate_bps: u32,
    pub frame_ms: u32,
    pub dtx: bool,
    pub fec: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VoiceState {
    pub user_id: Uuid,
    pub server_id: Uuid,
    pub channel_id: Option<Uuid>,
    pub self_mute: bool,
    pub self_deaf: bool,
    pub display_name: String,
    #[serde(default)]
    pub audio_quality: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct RtcToken {
    pub token: String,
    pub url: String,
    pub room: String,
    pub expires_at: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct VoiceJoin {
    pub rtc: RtcToken,
    pub state: VoiceState,
    pub audio: AudioProfile,
    pub max_audio_quality: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VoiceRoom {
    pub channel_id: Uuid,
    pub server_id: Uuid,
    pub name: String,
    pub server_name: String,
    pub kind: String,
    pub participant_count: u32,
}

#[derive(Debug, Serialize, Deserialize, Default)]
pub struct VoiceGuestRequest {
    pub display_name: String,
    #[serde(default)]
    pub self_mute: bool,
    #[serde(default)]
    pub self_deaf: bool,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct VoiceGuestSession {
    pub access_token: String,
    pub expires_in: u64,
    pub user: User,
    pub room: VoiceRoom,
    pub join: VoiceJoin,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Ready {
    pub session_id: String,
    pub user: User,
    pub servers: Vec<Server>,
    pub channels: Vec<Channel>,
    pub users: Vec<User>,
    pub heartbeat_interval_ms: u32,
    #[serde(default)]
    pub voice_states: Vec<VoiceState>,
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn shared_fixtures_decode() {
        let discovery: InstanceDiscovery = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/discovery.json"
        ))
        .unwrap();
        assert_eq!(discovery.protocol_version, PROTOCOL_VERSION);
        assert_eq!(discovery.max_attachment_bytes, MAX_ATTACHMENT_BYTES);
        assert_eq!(
            discovery.max_attachments_per_message,
            MAX_ATTACHMENTS_PER_MESSAGE as u32
        );
        let event: GatewayEnvelope = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/message-create.json"
        ))
        .unwrap();
        assert_eq!(event.seq.as_deref(), Some("9007199254740993"));
        let message: Message = serde_json::from_value(event.data).unwrap();
        assert_eq!(message.kind, "text");
        let voice: GatewayEnvelope = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/voice-state.json"
        ))
        .unwrap();
        assert_eq!(voice.event.as_deref(), Some("VOICE_STATE_UPDATE"));
        let state: VoiceState = serde_json::from_value(voice.data).unwrap();
        assert_eq!(state.display_name, "Ada");
        let rtc: RtcToken = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/rtc-token.json"
        ))
        .unwrap();
        assert_eq!(rtc.room, "voice:01950000-0000-7000-8000-000000000040");
        let room: VoiceRoom = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/voice-room.json"
        ))
        .unwrap();
        assert_eq!(room.kind, "voice");
        assert_eq!(room.participant_count, 2);
        let guest: VoiceGuestSession = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/voice-guest-join.json"
        ))
        .unwrap();
        assert_eq!(guest.room.channel_id, room.channel_id);
        assert_eq!(guest.join.state.display_name, "Ada");
        let user_event: GatewayEnvelope = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/user-update.json"
        ))
        .unwrap();
        assert_eq!(user_event.event.as_deref(), Some("USER_UPDATE"));
        let user: User = serde_json::from_value(user_event.data).unwrap();
        assert_eq!(user.username, "ada");
        assert_eq!(user.avatar.as_ref().map(|a| a.animated), Some(true));
        assert!(user.banner.is_none());
        let deleted: GatewayEnvelope = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/message-delete.json"
        ))
        .unwrap();
        assert_eq!(deleted.event.as_deref(), Some("MESSAGE_DELETE"));
        let gone: MessageDelete = serde_json::from_value(deleted.data).unwrap();
        assert_eq!(gone.id.to_string(), "01950000-0000-7000-8000-000000000010");
        let typing: GatewayEnvelope = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/typing-start.json"
        ))
        .unwrap();
        assert_eq!(typing.event.as_deref(), Some("TYPING_START"));
        assert!(typing.seq.is_none());
        let start: Typing = serde_json::from_value(typing.data).unwrap();
        assert_eq!(start.display_name, "Ada");
        let dm: GatewayEnvelope = serde_json::from_str(include_str!(
            "../../../../docs/protocol/fixtures/dm-open.json"
        ))
        .unwrap();
        assert_eq!(dm.event.as_deref(), Some("CHANNEL_CREATE"));
        let channel: Channel = serde_json::from_value(dm.data).unwrap();
        assert_eq!(channel.kind, "dm");
        assert!(channel.server_id.is_none());
        assert_eq!(channel.participants.as_ref().map(Vec::len), Some(2));
    }
}
