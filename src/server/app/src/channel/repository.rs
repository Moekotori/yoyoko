use crate::store::StoreError;
use async_trait::async_trait;
use chat_domain::{channel::Channel, voice::AudioQuality};
use uuid::Uuid;

#[async_trait]
pub trait ChannelManagementStore: Send + Sync {
    async fn update_channel(
        &self,
        id: Uuid,
        name: Option<String>,
        quality: Option<AudioQuality>,
    ) -> Result<Channel, StoreError>;
    async fn delete_channel(&self, id: Uuid) -> Result<(), StoreError>;
}
