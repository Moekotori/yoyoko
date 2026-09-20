use crate::voice::AudioQuality;
use uuid::Uuid;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ChannelKind {
    Text,
    Voice,
}

#[derive(Debug, Clone)]
pub struct Channel {
    pub id: Uuid,
    pub server_id: Uuid,
    pub name: String,
    pub kind: ChannelKind,
    pub audio_quality: AudioQuality,
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
        }
    }
    pub fn parse(value: &str) -> Option<Self> {
        match value {
            "text" => Some(Self::Text),
            "voice" => Some(Self::Voice),
            _ => None,
        }
    }
}
