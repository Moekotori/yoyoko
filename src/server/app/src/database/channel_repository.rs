use crate::channel::repository::{ChannelRepository, RepositoryError};
use chat_domain::channel::{Channel, ChannelKind};
use sqlx::{PgPool, Row};
use uuid::Uuid;

pub struct PgChannelRepository(pub PgPool);
impl ChannelRepository for PgChannelRepository {
    async fn find(&self, id: Uuid) -> Result<Option<Channel>, RepositoryError> {
        let row = sqlx::query("SELECT id, server_id, name, kind FROM channels WHERE id=$1")
            .bind(id)
            .fetch_optional(&self.0)
            .await
            .map_err(|_| RepositoryError::Unavailable)?;
        row.map(|row| {
            let kind: String = row
                .try_get("kind")
                .map_err(|_| RepositoryError::Unavailable)?;
            Ok(Channel {
                id: row
                    .try_get("id")
                    .map_err(|_| RepositoryError::Unavailable)?,
                server_id: row
                    .try_get("server_id")
                    .map_err(|_| RepositoryError::Unavailable)?,
                name: row
                    .try_get("name")
                    .map_err(|_| RepositoryError::Unavailable)?,
                kind: match kind.as_str() {
                    "text" => ChannelKind::Text,
                    "voice" => ChannelKind::Voice,
                    _ => return Err(RepositoryError::Unavailable),
                },
            })
        })
        .transpose()
    }
}
