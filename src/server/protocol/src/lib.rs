use serde::{Deserialize, Serialize};
use serde_json::Value;
use uuid::Uuid;

pub const PROTOCOL_VERSION: u32 = 1;
pub const API_VERSION: u32 = 1;
pub const MAX_GATEWAY_BYTES: usize = 65_536;

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

#[derive(Debug, Serialize, Deserialize)]
pub struct GatewayEnvelope {
    pub op: String,
    pub event: Option<String>,
    // Decimal strings prevent precision loss in future JS clients.
    pub seq: Option<String>,
    pub data: Value,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Identify {
    pub protocol_version: u32,
    pub access_token: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct ApiError {
    pub code: String,
    pub message: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Attachment {
    pub id: Uuid,
    pub file_name: String,
    pub mime_type: String,
    pub size: u64,
    pub download_url: String,
    pub thumbnail_url: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
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
    }
}
