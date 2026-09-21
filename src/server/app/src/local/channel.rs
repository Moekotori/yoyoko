use super::*;
use crate::channel::repository::ChannelManagementStore;
#[async_trait]
impl ChannelManagementStore for LocalStore {
    async fn update_channel(
        &self,
        id: Uuid,
        name: Option<String>,
        quality: Option<chat_domain::voice::AudioQuality>,
    ) -> Result<Channel, StoreError> {
        let mut inner = self.0.lock().await;
        let row = inner.channels.get_mut(&id).ok_or(StoreError::NotFound)?;
        if let Some(name) = name {
            row.name = name;
        }
        if let Some(quality) = quality {
            row.audio_quality = quality.as_str().into();
        }
        let channel = inner.channel(id).ok_or(StoreError::NotFound)?;
        inner.persist();
        Ok(channel)
    }
    async fn delete_channel(&self, id: Uuid) -> Result<(), StoreError> {
        let mut inner = self.0.lock().await;
        inner.channels.remove(&id).ok_or(StoreError::NotFound)?;
        if let Some(messages) = inner.by_channel.remove(&id) {
            for message in messages.values() {
                inner.messages.remove(message);
            }
            inner
                .idempotency
                .retain(|_, message| !messages.contains_key(message));
        }
        inner.persist();
        Ok(())
    }
}
