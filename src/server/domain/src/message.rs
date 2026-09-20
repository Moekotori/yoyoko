use uuid::Uuid;

#[derive(Debug, Clone)]
pub struct Attachment {
    pub id: Uuid,
    pub file_name: String,
    pub mime_type: String,
    pub size: u64,
    pub object_key: Uuid,
    pub thumbnail_key: Option<Uuid>,
}

#[derive(Debug, Clone)]
pub struct Message {
    pub id: Uuid,
    pub channel_id: Uuid,
    pub author_id: Uuid,
    pub kind: String,
    pub content: Option<String>,
    pub created_at: String,
    pub edited_at: Option<String>,
    pub reply_to: Option<Uuid>,
    pub attachments: Vec<Attachment>,
}
