use super::*;
use crate::channel::repository::ChannelManagementStore;
#[async_trait]
impl ChannelManagementStore for PgStore {
    async fn update_channel(
        &self,
        id: Uuid,
        name: Option<String>,
        quality: Option<chat_domain::voice::AudioQuality>,
    ) -> Result<Channel, StoreError> {
        let row = map_db(sqlx::query("UPDATE channels SET name=COALESCE($2,name), audio_quality=COALESCE($3,audio_quality) WHERE id=$1 RETURNING id, server_id, name, kind, audio_quality")
            .bind(id).bind(name).bind(quality.map(|value| value.as_str())).fetch_optional(&self.0).await)?;
        channel_from(&row.ok_or(StoreError::NotFound)?)
    }
    async fn delete_channel(&self, id: Uuid) -> Result<(), StoreError> {
        let result = map_db(
            sqlx::query("DELETE FROM channels WHERE id=$1")
                .bind(id)
                .execute(&self.0)
                .await,
        )?;
        if result.rows_affected() == 0 {
            return Err(StoreError::NotFound);
        }
        Ok(())
    }
}
