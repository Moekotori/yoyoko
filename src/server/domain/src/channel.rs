use crate::voice::AudioQuality;
use uuid::Uuid;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ChannelKind {
    Text,
    Voice,
    Direct,
}

#[derive(Debug, Clone)]
pub struct Channel {
    pub id: Uuid,
    pub server_id: Option<Uuid>,
    pub name: String,
    pub kind: ChannelKind,
    pub audio_quality: AudioQuality,
    pub participants: Vec<Uuid>,
}

#[derive(Debug, Clone)]
pub struct Server {
    pub id: Uuid,
    pub name: String,
    pub owner_id: Uuid,
    pub invite_code: String,
    pub blocked_words: Vec<String>,
    pub cooldown_seconds: u32,
}

#[derive(Debug, Clone, Copy)]
pub struct Actor {
    pub user_id: Uuid,
}

impl ChannelKind {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::Text => "text",
            Self::Voice => "voice",
            Self::Direct => "dm",
        }
    }
    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "text" => Some(Self::Text),
            "voice" => Some(Self::Voice),
            "dm" => Some(Self::Direct),
            _ => None,
        }
    }
}

impl Channel {
    pub fn in_server(
        id: Uuid,
        server_id: Uuid,
        name: impl Into<String>,
        kind: ChannelKind,
        audio_quality: AudioQuality,
    ) -> Self {
        Self {
            id,
            server_id: Some(server_id),
            name: name.into(),
            kind,
            audio_quality,
            participants: vec![],
        }
    }

    pub fn is_direct(&self) -> bool {
        self.kind == ChannelKind::Direct
    }
}
