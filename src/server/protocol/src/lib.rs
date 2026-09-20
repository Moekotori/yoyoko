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
pub const MAX_ATTACHMENTS_PER_MESSAGE: usize = 4;

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
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct User {
    pub id: Uuid,
    pub username: String,
    pub display_name: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct RegisterRequest {
    pub username: String,
    pub display_name: String,
    pub password: String,
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
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Channel {
    pub id: Uuid,
    pub server_id: Uuid,
    pub name: String,
    pub kind: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct CreateServerRequest {
    pub name: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct CreateChannelRequest {
    pub name: String,
    pub kind: String,
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
    pub attachments: Vec<Attachment>,
    pub embeds: Vec<Value>,
    pub reactions: Vec<Value>,
    pub encrypted_payload: Option<Value>,
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
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VoiceState {
    pub user_id: Uuid,
    pub server_id: Uuid,
    pub channel_id: Option<Uuid>,
    pub self_mute: bool,
    pub self_deaf: bool,
    pub display_name: String,
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
    }
}
