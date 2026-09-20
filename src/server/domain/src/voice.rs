use uuid::Uuid;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VoiceState {
    pub user_id: Uuid,
    pub server_id: Uuid,
    pub channel_id: Uuid,
    pub self_mute: bool,
    pub self_deaf: bool,
    pub display_name: String,
}

impl VoiceState {
    pub fn room_name(channel_id: Uuid) -> String {
        format!("voice:{channel_id}")
    }
}
