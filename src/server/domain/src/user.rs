use crate::message::Attachment;
use uuid::Uuid;

#[derive(Debug, Clone)]
pub struct User {
    pub id: Uuid,
    pub username: String,
    pub display_name: String,
    pub avatar: Option<Attachment>,
    pub avatar_animated: bool,
    pub banner: Option<Attachment>,
    pub banner_animated: bool,
}
