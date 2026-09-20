use uuid::Uuid;

#[derive(Debug, Clone, PartialEq, Eq)]
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
}

#[derive(Debug, Clone, Copy)]
pub struct Actor {
    pub user_id: Uuid,
}
