mod channel;

use crate::store::{
    GatewaySession, NewServer, OutboxEvent, PermissionSnapshot, SessionRecord, Store, StoreError,
};
use async_trait::async_trait;
use chat_domain::{
    channel::{Channel, ChannelKind, Server},
    message::{Attachment, Message},
    permission::Permissions,
    user::User,
};
use chrono::{DateTime, SecondsFormat, Utc};
use serde_json::Value;
use sqlx::{PgPool, Postgres, Row, Transaction, postgres::PgRow};
use std::collections::HashMap;
use uuid::Uuid;

pub struct PgStore(pub PgPool);

fn map_db<T>(result: Result<T, sqlx::Error>) -> Result<T, StoreError> {
    result.map_err(|err| match err {
        sqlx::Error::Database(db) if db.constraint().is_some() => {
            StoreError::Conflict("Conflict.".into())
        }
        _ => StoreError::Unavailable,
    })
}

fn rfc3339(value: DateTime<Utc>) -> String {
    value.to_rfc3339_opts(SecondsFormat::Secs, true)
}

fn ts(unix: i64) -> DateTime<Utc> {
    DateTime::from_timestamp(unix, 0).unwrap_or_else(Utc::now)
}

fn parse_bits(value: String) -> Permissions {
    Permissions(value.parse().unwrap_or(0))
}

const USER_SELECT: &str = "u.id, u.username, u.display_name, u.avatar_animated,
       a.id AS avatar_att_id, a.file_name, a.mime_type, a.size_bytes, a.object_key, a.thumbnail_key";

fn user_from(row: &PgRow) -> Result<User, StoreError> {
    let avatar = match row
        .try_get::<Option<Uuid>, _>("avatar_att_id")
        .map_err(|_| StoreError::Unavailable)?
    {
        Some(id) => Some(Attachment {
            id,
            file_name: row
                .try_get("file_name")
                .map_err(|_| StoreError::Unavailable)?,
            mime_type: row
                .try_get("mime_type")
                .map_err(|_| StoreError::Unavailable)?,
            size: row
                .try_get::<i64, _>("size_bytes")
                .map_err(|_| StoreError::Unavailable)? as u64,
            object_key: row
                .try_get("object_key")
                .map_err(|_| StoreError::Unavailable)?,
            thumbnail_key: row
                .try_get("thumbnail_key")
                .map_err(|_| StoreError::Unavailable)?,
        }),
        None => None,
    };
    Ok(User {
        id: row.try_get("id").map_err(|_| StoreError::Unavailable)?,
        username: row
            .try_get("username")
            .map_err(|_| StoreError::Unavailable)?,
        display_name: row
            .try_get("display_name")
            .map_err(|_| StoreError::Unavailable)?,
        avatar,
        avatar_animated: row
            .try_get("avatar_animated")
            .map_err(|_| StoreError::Unavailable)?,
    })
}

fn channel_from(row: &PgRow) -> Result<Channel, StoreError> {
    let kind: String = row.try_get("kind").map_err(|_| StoreError::Unavailable)?;
    let quality: String = row
        .try_get("audio_quality")
        .unwrap_or_else(|_| chat_domain::voice::AudioQuality::DEFAULT.as_str().into());
    Ok(Channel {
        id: row.try_get("id").map_err(|_| StoreError::Unavailable)?,
        server_id: row
            .try_get("server_id")
            .map_err(|_| StoreError::Unavailable)?,
        name: row.try_get("name").map_err(|_| StoreError::Unavailable)?,
        kind: ChannelKind::parse(&kind).ok_or(StoreError::Unavailable)?,
        audio_quality: chat_domain::voice::AudioQuality::parse(&quality)
            .unwrap_or(chat_domain::voice::AudioQuality::DEFAULT),
    })
}

fn server_from(row: &PgRow) -> Result<Server, StoreError> {
    let words: serde_json::Value = row
        .try_get("blocked_words")
        .unwrap_or_else(|_| serde_json::json!([]));
    Ok(Server {
        id: row.try_get("id").map_err(|_| StoreError::Unavailable)?,
        name: row.try_get("name").map_err(|_| StoreError::Unavailable)?,
        owner_id: row
            .try_get("owner_id")
            .map_err(|_| StoreError::Unavailable)?,
        invite_code: row
            .try_get("invite_code")
            .map_err(|_| StoreError::Unavailable)?,
        blocked_words: serde_json::from_value(words).unwrap_or_default(),
        cooldown_seconds: row
            .try_get::<i32, _>("cooldown_seconds")
            .unwrap_or(0)
            .max(0) as u32,
    })
}

fn attachment_from(row: &PgRow) -> Result<Attachment, StoreError> {
    Ok(Attachment {
        id: row.try_get("id").map_err(|_| StoreError::Unavailable)?,
        file_name: row
            .try_get("file_name")
            .map_err(|_| StoreError::Unavailable)?,
        mime_type: row
            .try_get("mime_type")
            .map_err(|_| StoreError::Unavailable)?,
        size: row
            .try_get::<i64, _>("size_bytes")
            .map_err(|_| StoreError::Unavailable)? as u64,
        object_key: row
            .try_get("object_key")
            .map_err(|_| StoreError::Unavailable)?,
        thumbnail_key: row
            .try_get("thumbnail_key")
            .map_err(|_| StoreError::Unavailable)?,
    })
}

fn message_from(
    row: &PgRow,
    attachments: Vec<Attachment>,
    mentions: Vec<Uuid>,
) -> Result<Message, StoreError> {
    let created: DateTime<Utc> = row
        .try_get("created_at")
        .map_err(|_| StoreError::Unavailable)?;
    let edited: Option<DateTime<Utc>> = row
        .try_get("edited_at")
        .map_err(|_| StoreError::Unavailable)?;
    Ok(Message {
        id: row.try_get("id").map_err(|_| StoreError::Unavailable)?,
        channel_id: row
            .try_get("channel_id")
            .map_err(|_| StoreError::Unavailable)?,
        author_id: row
            .try_get("author_id")
            .map_err(|_| StoreError::Unavailable)?,
        kind: row.try_get("kind").map_err(|_| StoreError::Unavailable)?,
        content: row
            .try_get("content")
            .map_err(|_| StoreError::Unavailable)?,
        created_at: rfc3339(created),
        edited_at: edited.map(rfc3339),
        reply_to: row
            .try_get("reply_to")
            .map_err(|_| StoreError::Unavailable)?,
        mentions,
        attachments,
    })
}

async fn load_mentions(pool: &PgPool, ids: &[Uuid]) -> Result<HashMap<Uuid, Vec<Uuid>>, StoreError> {
    let mut grouped: HashMap<Uuid, Vec<Uuid>> = HashMap::new();
    if ids.is_empty() {
        return Ok(grouped);
    }
    let rows = map_db(
        sqlx::query("SELECT message_id, user_id FROM message_mentions WHERE message_id = ANY($1)")
            .bind(ids)
            .fetch_all(pool)
            .await,
    )?;
    for row in rows {
        let message_id: Uuid = row
            .try_get("message_id")
            .map_err(|_| StoreError::Unavailable)?;
        let user_id: Uuid = row.try_get("user_id").map_err(|_| StoreError::Unavailable)?;
        grouped.entry(message_id).or_default().push(user_id);
    }
    Ok(grouped)
}

async fn load_attachment_rows(pool: &PgPool, ids: &[Uuid]) -> Result<Vec<PgRow>, StoreError> {
    if ids.is_empty() {
        return Ok(vec![]);
    }
    map_db(
        sqlx::query(
            "SELECT id, file_name, mime_type, size_bytes, object_key, thumbnail_key, message_id
             FROM attachments WHERE message_id = ANY($1)",
        )
        .bind(ids)
        .fetch_all(pool)
        .await,
    )
}

async fn enqueue_tx(
    tx: &mut Transaction<'_, Postgres>,
    members: &[Uuid],
    event: &str,
    payload: &Value,
) -> Result<Vec<OutboxEvent>, StoreError> {
    let mut events = Vec::with_capacity(members.len());
    for user_id in members {
        let row = map_db(
            sqlx::query(
                "INSERT INTO event_outbox (user_id, event, payload) VALUES ($1,$2,$3) RETURNING seq",
            )
            .bind(user_id)
            .bind(event)
            .bind(payload)
            .fetch_one(&mut **tx)
            .await,
        )?;
        let seq: i64 = row.try_get("seq").map_err(|_| StoreError::Unavailable)?;
        events.push(OutboxEvent {
            seq,
            user_id: *user_id,
            event: event.into(),
            payload: payload.clone(),
        });
    }
    Ok(events)
}

async fn replace_mentions_tx(
    tx: &mut Transaction<'_, Postgres>,
    message_id: Uuid,
    mentions: &[Uuid],
) -> Result<(), StoreError> {
    map_db(
        sqlx::query("DELETE FROM message_mentions WHERE message_id=$1")
            .bind(message_id)
            .execute(&mut **tx)
            .await,
    )?;
    for user_id in mentions {
        map_db(
            sqlx::query("INSERT INTO message_mentions (message_id, user_id) VALUES ($1,$2)")
                .bind(message_id)
                .bind(user_id)
                .execute(&mut **tx)
                .await,
        )?;
    }
    Ok(())
}

#[async_trait]
impl Store for PgStore {
    async fn ping(&self) -> Result<(), StoreError> {
        map_db(sqlx::query("SELECT 1").execute(&self.0).await).map(|_| ())
    }

    async fn create_user(
        &self,
        id: Uuid,
        username: &str,
        display_name: &str,
        password_hash: &str,
    ) -> Result<User, StoreError> {
        let result = sqlx::query(
            "INSERT INTO users (id, username, display_name, password_hash) VALUES ($1,$2,$3,$4)",
        )
        .bind(id)
        .bind(username)
        .bind(display_name)
        .bind(password_hash)
        .execute(&self.0)
        .await;
        match result {
            Ok(_) => Ok(User {
                id,
                username: username.into(),
                display_name: display_name.into(),
                avatar: None,
                avatar_animated: false,
            }),
            Err(sqlx::Error::Database(db)) if db.constraint() == Some("users_username_key") => {
                Err(StoreError::Conflict("Username already taken.".into()))
            }
            Err(_) => Err(StoreError::Unavailable),
        }
    }

    async fn find_user(&self, id: Uuid) -> Result<Option<User>, StoreError> {
        let row = map_db(
            sqlx::query(&format!(
                "SELECT {USER_SELECT} FROM users u LEFT JOIN attachments a ON a.id=u.avatar_id WHERE u.id=$1"
            ))
            .bind(id)
            .fetch_optional(&self.0)
            .await,
        )?;
        row.as_ref().map(user_from).transpose()
    }

    async fn find_user_by_username(
        &self,
        username: &str,
    ) -> Result<Option<(User, String)>, StoreError> {
        let row = map_db(
            sqlx::query(&format!(
                "SELECT {USER_SELECT}, u.password_hash FROM users u LEFT JOIN attachments a ON a.id=u.avatar_id WHERE u.username=$1"
            ))
            .bind(username)
            .fetch_optional(&self.0)
            .await,
        )?;
        row.map(|row| {
            Ok((
                user_from(&row)?,
                row.try_get("password_hash")
                    .map_err(|_| StoreError::Unavailable)?,
            ))
        })
        .transpose()
    }

    async fn update_user(
        &self,
        id: Uuid,
        username: &str,
        display_name: &str,
        avatar_id: Option<Uuid>,
        avatar_animated: bool,
    ) -> Result<User, StoreError> {
        let result = sqlx::query(
            "UPDATE users SET username=$2, display_name=$3, avatar_id=$4, avatar_animated=$5 WHERE id=$1",
        )
        .bind(id)
        .bind(username)
        .bind(display_name)
        .bind(avatar_id)
        .bind(avatar_animated)
        .execute(&self.0)
        .await;
        match result {
            Ok(done) if done.rows_affected() == 0 => Err(StoreError::NotFound),
            Ok(_) => self.find_user(id).await?.ok_or(StoreError::Unavailable),
            Err(sqlx::Error::Database(db)) if db.constraint() == Some("users_username_key") => {
                Err(StoreError::Conflict("Username already taken.".into()))
            }
            Err(_) => Err(StoreError::Unavailable),
        }
    }

    async fn avatar_owner(&self, attachment_id: Uuid) -> Result<Option<Uuid>, StoreError> {
        let row = map_db(
            sqlx::query("SELECT id FROM users WHERE avatar_id=$1")
                .bind(attachment_id)
                .fetch_optional(&self.0)
                .await,
        )?;
        row.map(|row| row.try_get("id").map_err(|_| StoreError::Unavailable))
            .transpose()
    }

    async fn shares_community(&self, a: Uuid, b: Uuid) -> Result<bool, StoreError> {
        if a == b {
            return Ok(true);
        }
        let row = map_db(
            sqlx::query(
                "SELECT EXISTS(
                    SELECT 1 FROM members mine
                    JOIN members theirs ON theirs.server_id=mine.server_id
                    WHERE mine.user_id=$1 AND theirs.user_id=$2
                 )",
            )
            .bind(a)
            .bind(b)
            .fetch_one(&self.0)
            .await,
        )?;
        row.try_get(0).map_err(|_| StoreError::Unavailable)
    }

    async fn create_session(&self, session: SessionRecord) -> Result<(), StoreError> {
        map_db(
            sqlx::query(
                "INSERT INTO sessions (id, user_id, refresh_token_hash, expires_at) VALUES ($1,$2,$3,$4)",
            )
            .bind(session.id)
            .bind(session.user_id)
            .bind(session.refresh_hash.as_slice())
            .bind(ts(session.expires_at))
            .execute(&self.0)
            .await,
        )
        .map(|_| ())
    }

    async fn find_session_by_refresh(
        &self,
        hash: [u8; 32],
    ) -> Result<Option<SessionRecord>, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT id, user_id, refresh_token_hash, EXTRACT(EPOCH FROM expires_at)::bigint AS exp,
                        revoked_at IS NOT NULL AS revoked
                 FROM sessions WHERE refresh_token_hash=$1",
            )
            .bind(hash.as_slice())
            .fetch_optional(&self.0)
            .await,
        )?;
        row.map(|row| {
            let bytes: Vec<u8> = row
                .try_get("refresh_token_hash")
                .map_err(|_| StoreError::Unavailable)?;
            let mut refresh_hash = [0u8; 32];
            if bytes.len() != 32 {
                return Err(StoreError::Unavailable);
            }
            refresh_hash.copy_from_slice(&bytes);
            Ok(SessionRecord {
                id: row.try_get("id").map_err(|_| StoreError::Unavailable)?,
                user_id: row
                    .try_get("user_id")
                    .map_err(|_| StoreError::Unavailable)?,
                refresh_hash,
                expires_at: row.try_get("exp").map_err(|_| StoreError::Unavailable)?,
                revoked: row
                    .try_get("revoked")
                    .map_err(|_| StoreError::Unavailable)?,
            })
        })
        .transpose()
    }

    async fn rotate_session(
        &self,
        id: Uuid,
        hash: [u8; 32],
        expires_at: i64,
    ) -> Result<(), StoreError> {
        map_db(
            sqlx::query(
                "UPDATE sessions SET refresh_token_hash=$2, expires_at=$3, revoked_at=NULL WHERE id=$1",
            )
            .bind(id)
            .bind(hash.as_slice())
            .bind(ts(expires_at))
            .execute(&self.0)
            .await,
        )
        .map(|_| ())
    }

    async fn revoke_session(&self, id: Uuid) -> Result<(), StoreError> {
        map_db(
            sqlx::query("UPDATE sessions SET revoked_at=now() WHERE id=$1")
                .bind(id)
                .execute(&self.0)
                .await,
        )
        .map(|_| ())
    }

    async fn create_server(
        &self,
        owner: Uuid,
        server: Server,
        base_role: Uuid,
        owner_role: Uuid,
        channel: Channel,
    ) -> Result<NewServer, StoreError> {
        let mut tx = map_db(self.0.begin().await)?;
        map_db(
            sqlx::query(
                "INSERT INTO servers (id, owner_id, name, blocked_words, cooldown_seconds) VALUES ($1,$2,$3,$4::jsonb,$5)",
            )
                .bind(server.id)
                .bind(owner)
                .bind(&server.name)
                .bind(serde_json::to_value(&server.blocked_words).unwrap_or_else(|_| serde_json::json!([])))
                .bind(server.cooldown_seconds as i32)
                .execute(&mut *tx)
                .await,
        )?;
        map_db(
            sqlx::query("INSERT INTO members (server_id, user_id) VALUES ($1,$2)")
                .bind(server.id)
                .bind(owner)
                .execute(&mut *tx)
                .await,
        )?;
        let view_send = (Permissions::VIEW_CHANNEL.0
            | Permissions::SEND_MESSAGE.0
            | Permissions::CONNECT_VOICE.0
            | Permissions::SPEAK.0)
            .to_string();
        map_db(
            sqlx::query(
                "INSERT INTO roles (id, server_id, name, permissions, is_base) VALUES ($1,$2,'Member',$3::numeric,true)",
            )
            .bind(base_role)
            .bind(server.id)
            .bind(&view_send)
            .execute(&mut *tx)
            .await,
        )?;
        map_db(
            sqlx::query(
                "INSERT INTO roles (id, server_id, name, permissions, is_base) VALUES ($1,$2,'Owner',$3::numeric,false)",
            )
            .bind(owner_role)
            .bind(server.id)
            .bind(Permissions::ADMINISTRATOR.0.to_string())
            .execute(&mut *tx)
            .await,
        )?;
        map_db(
            sqlx::query(
                "INSERT INTO member_roles (server_id, user_id, role_id) VALUES ($1,$2,$3),($1,$2,$4)",
            )
            .bind(server.id)
            .bind(owner)
            .bind(base_role)
            .bind(owner_role)
            .execute(&mut *tx)
            .await,
        )?;
        map_db(
            sqlx::query(
                "INSERT INTO channels (id, server_id, name, kind, position, audio_quality) VALUES ($1,$2,$3,$4,0,$5)",
            )
            .bind(channel.id)
            .bind(server.id)
            .bind(&channel.name)
            .bind(channel.kind.as_str())
            .bind(channel.audio_quality.as_str())
            .execute(&mut *tx)
            .await,
        )?;
        map_db(
            sqlx::query("INSERT INTO invites (code, server_id, created_by) VALUES ($1,$2,$3)")
                .bind(&server.invite_code)
                .bind(server.id)
                .bind(owner)
                .execute(&mut *tx)
                .await,
        )?;
        map_db(tx.commit().await)?;
        Ok(NewServer { server, channel })
    }

    async fn list_servers(&self, user: Uuid) -> Result<Vec<Server>, StoreError> {
        let rows = map_db(
            sqlx::query(
                "SELECT s.id, s.name, s.owner_id, i.code AS invite_code, s.blocked_words, s.cooldown_seconds
                 FROM members m
                 JOIN servers s ON s.id=m.server_id
                 JOIN invites i ON i.server_id=s.id
                 WHERE m.user_id=$1
                 ORDER BY s.created_at",
            )
            .bind(user)
            .fetch_all(&self.0)
            .await,
        )?;
        rows.iter().map(server_from).collect()
    }

    async fn list_channels(&self, server: Uuid) -> Result<Vec<Channel>, StoreError> {
        let rows = map_db(
            sqlx::query(
                "SELECT id, server_id, name, kind, audio_quality FROM channels WHERE server_id=$1 ORDER BY position, id",
            )
            .bind(server)
            .fetch_all(&self.0)
            .await,
        )?;
        rows.iter().map(channel_from).collect()
    }

    async fn find_channel(&self, id: Uuid) -> Result<Option<Channel>, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT id, server_id, name, kind, audio_quality FROM channels WHERE id=$1",
            )
            .bind(id)
            .fetch_optional(&self.0)
            .await,
        )?;
        row.as_ref().map(channel_from).transpose()
    }

    async fn find_server(&self, id: Uuid) -> Result<Option<Server>, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT s.id, s.name, s.owner_id, i.code AS invite_code, s.blocked_words, s.cooldown_seconds
                 FROM servers s JOIN invites i ON i.server_id=s.id WHERE s.id=$1",
            )
            .bind(id)
            .fetch_optional(&self.0)
            .await,
        )?;
        row.as_ref().map(server_from).transpose()
    }

    async fn update_moderation(
        &self,
        id: Uuid,
        blocked_words: Vec<String>,
        cooldown_seconds: u32,
    ) -> Result<Server, StoreError> {
        map_db(
            sqlx::query(
                "UPDATE servers SET blocked_words=$2::jsonb, cooldown_seconds=$3 WHERE id=$1",
            )
            .bind(id)
            .bind(serde_json::to_value(&blocked_words).unwrap_or_else(|_| serde_json::json!([])))
            .bind(cooldown_seconds as i32)
            .execute(&self.0)
            .await,
        )?;
        self.find_server(id)
            .await?
            .ok_or(StoreError::NotFound)
    }

    async fn last_user_message_at(
        &self,
        channel: Uuid,
        user: Uuid,
    ) -> Result<Option<i64>, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT EXTRACT(EPOCH FROM created_at)::bigint AS ts
                 FROM messages
                 WHERE channel_id=$1 AND author_id=$2 AND deleted_at IS NULL
                 ORDER BY id DESC LIMIT 1",
            )
            .bind(channel)
            .bind(user)
            .fetch_optional(&self.0)
            .await,
        )?;
        Ok(row.and_then(|row| row.try_get("ts").ok()))
    }

    async fn add_channel(&self, channel: Channel) -> Result<Channel, StoreError> {
        map_db(
            sqlx::query(
                "INSERT INTO channels (id, server_id, name, kind, position, audio_quality)
                 VALUES ($1,$2,$3,$4,COALESCE((SELECT MAX(position)+1 FROM channels WHERE server_id=$2),0),$5)",
            )
            .bind(channel.id)
            .bind(channel.server_id)
            .bind(&channel.name)
            .bind(channel.kind.as_str())
            .bind(channel.audio_quality.as_str())
            .execute(&self.0)
            .await,
        )?;
        Ok(channel)
    }

    async fn set_channel_audio_quality(
        &self,
        id: Uuid,
        quality: chat_domain::voice::AudioQuality,
    ) -> Result<Channel, StoreError> {
        map_db(
            sqlx::query("UPDATE channels SET audio_quality=$2 WHERE id=$1")
                .bind(id)
                .bind(quality.as_str())
                .execute(&self.0)
                .await,
        )?;
        self.find_channel(id).await?.ok_or(StoreError::NotFound)
    }

    async fn join_invite(&self, user: Uuid, code: &str) -> Result<Server, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT s.id, s.name, s.owner_id, i.code AS invite_code, s.blocked_words, s.cooldown_seconds
                 FROM invites i JOIN servers s ON s.id=i.server_id WHERE i.code=$1",
            )
            .bind(code)
            .fetch_optional(&self.0)
            .await,
        )?
        .ok_or(StoreError::NotFound)?;
        let server = server_from(&row)?;
        let mut tx = map_db(self.0.begin().await)?;
        map_db(
            sqlx::query(
                "INSERT INTO members (server_id, user_id) VALUES ($1,$2) ON CONFLICT DO NOTHING",
            )
            .bind(server.id)
            .bind(user)
            .execute(&mut *tx)
            .await,
        )?;
        map_db(
            sqlx::query(
                "INSERT INTO member_roles (server_id, user_id, role_id)
                 SELECT $1,$2,id FROM roles WHERE server_id=$1 AND is_base
                 ON CONFLICT DO NOTHING",
            )
            .bind(server.id)
            .bind(user)
            .execute(&mut *tx)
            .await,
        )?;
        map_db(tx.commit().await)?;
        Ok(server)
    }

    async fn list_members(&self, server: Uuid) -> Result<Vec<Uuid>, StoreError> {
        let rows = map_db(
            sqlx::query("SELECT user_id FROM members WHERE server_id=$1")
                .bind(server)
                .fetch_all(&self.0)
                .await,
        )?;
        rows.iter()
            .map(|row| row.try_get("user_id").map_err(|_| StoreError::Unavailable))
            .collect()
    }

    async fn list_visible_users(&self, user: Uuid) -> Result<Vec<User>, StoreError> {
        let rows = map_db(
            sqlx::query(&format!(
                "SELECT DISTINCT {USER_SELECT}
                 FROM members mine
                 JOIN members theirs ON theirs.server_id=mine.server_id
                 JOIN users u ON u.id=theirs.user_id
                 LEFT JOIN attachments a ON a.id=u.avatar_id
                 WHERE mine.user_id=$1"
            ))
            .bind(user)
            .fetch_all(&self.0)
            .await,
        )?;
        rows.iter().map(user_from).collect()
    }

    async fn permissions(
        &self,
        user: Uuid,
        channel: Uuid,
    ) -> Result<PermissionSnapshot, StoreError> {
        let channel_row = self
            .find_channel(channel)
            .await?
            .ok_or(StoreError::NotFound)?;
        let member: bool = map_db(
            sqlx::query("SELECT EXISTS(SELECT 1 FROM members WHERE server_id=$1 AND user_id=$2)")
                .bind(channel_row.server_id)
                .bind(user)
                .fetch_one(&self.0)
                .await,
        )?
        .try_get(0)
        .map_err(|_| StoreError::Unavailable)?;
        let base = map_db(
            sqlx::query(
                "SELECT permissions::text AS permissions FROM roles WHERE server_id=$1 AND is_base",
            )
            .bind(channel_row.server_id)
            .fetch_optional(&self.0)
            .await,
        )?
        .map(|row| parse_bits(row.try_get("permissions").unwrap_or_else(|_| "0".into())))
        .unwrap_or(Permissions(0));
        let role_rows = map_db(
            sqlx::query(
                "SELECT r.permissions::text AS permissions
                 FROM member_roles mr JOIN roles r ON r.id=mr.role_id
                 WHERE mr.server_id=$1 AND mr.user_id=$2",
            )
            .bind(channel_row.server_id)
            .bind(user)
            .fetch_all(&self.0)
            .await,
        )?;
        let roles = role_rows
            .iter()
            .map(|row| parse_bits(row.try_get("permissions").unwrap_or_else(|_| "0".into())))
            .collect();
        Ok(PermissionSnapshot {
            member,
            base,
            roles,
            everyone: (Permissions(0), Permissions(0)),
            role_overrides: vec![],
            member_override: (Permissions(0), Permissions(0)),
        })
    }

    async fn insert_attachment(
        &self,
        attachment: Attachment,
        uploader: Uuid,
    ) -> Result<(), StoreError> {
        map_db(
            sqlx::query(
                "INSERT INTO attachments (id, message_id, uploader_id, object_key, file_name, mime_type, size_bytes, thumbnail_key)
                 VALUES ($1,NULL,$2,$3,$4,$5,$6,$7)",
            )
            .bind(attachment.id)
            .bind(uploader)
            .bind(attachment.object_key)
            .bind(attachment.file_name)
            .bind(attachment.mime_type)
            .bind(attachment.size as i64)
            .bind(attachment.thumbnail_key)
            .execute(&self.0)
            .await,
        )
        .map(|_| ())
    }

    async fn get_attachment(
        &self,
        id: Uuid,
    ) -> Result<Option<(Attachment, Uuid, Option<Uuid>)>, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT id, file_name, mime_type, size_bytes, object_key, thumbnail_key, uploader_id, message_id
                 FROM attachments WHERE id=$1",
            )
            .bind(id)
            .fetch_optional(&self.0)
            .await,
        )?;
        row.map(|row| {
            Ok((
                attachment_from(&row)?,
                row.try_get("uploader_id")
                    .map_err(|_| StoreError::Unavailable)?,
                row.try_get("message_id")
                    .map_err(|_| StoreError::Unavailable)?,
            ))
        })
        .transpose()
    }

    async fn message_channel(&self, message_id: Uuid) -> Result<Option<Uuid>, StoreError> {
        let row = map_db(
            sqlx::query("SELECT channel_id FROM messages WHERE id=$1")
                .bind(message_id)
                .fetch_optional(&self.0)
                .await,
        )?;
        row.map(|row| {
            row.try_get("channel_id")
                .map_err(|_| StoreError::Unavailable)
        })
        .transpose()
    }

    async fn bind_attachments(
        &self,
        message_id: Uuid,
        uploader: Uuid,
        ids: &[Uuid],
    ) -> Result<Vec<Attachment>, StoreError> {
        let mut bound = Vec::new();
        for id in ids {
            let result = map_db(
                sqlx::query(
                    "UPDATE attachments SET message_id=$1
                     WHERE id=$2 AND uploader_id=$3 AND message_id IS NULL
                       AND NOT EXISTS (SELECT 1 FROM users WHERE avatar_id=$2)
                     RETURNING id, file_name, mime_type, size_bytes, object_key, thumbnail_key",
                )
                .bind(message_id)
                .bind(id)
                .bind(uploader)
                .fetch_optional(&self.0)
                .await,
            )?;
            bound.push(attachment_from(
                &result.ok_or(StoreError::Conflict("Attachment already used.".into()))?,
            )?);
        }
        Ok(bound)
    }

    async fn find_idempotent(&self, user: Uuid, key: &str) -> Result<Option<Message>, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT m.id, m.channel_id, m.author_id, m.kind, m.content, m.created_at, m.edited_at, m.reply_to
                 FROM idempotency_keys k JOIN messages m ON m.id=k.message_id
                 WHERE k.user_id=$1 AND k.key=$2",
            )
            .bind(user)
            .bind(key)
            .fetch_optional(&self.0)
            .await,
        )?;
        match row {
            None => Ok(None),
            Some(row) => {
                let id: Uuid = row.try_get("id").map_err(|_| StoreError::Unavailable)?;
                let attach_rows = load_attachment_rows(&self.0, &[id]).await?;
                let attachments = attach_rows
                    .iter()
                    .map(attachment_from)
                    .collect::<Result<Vec<_>, _>>()?;
                let mentions = load_mentions(&self.0, &[id])
                    .await?
                    .remove(&id)
                    .unwrap_or_default();
                Ok(Some(message_from(&row, attachments, mentions)?))
            }
        }
    }

    async fn get_message(&self, id: Uuid) -> Result<Option<Message>, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT id, channel_id, author_id, kind, content, created_at, edited_at, reply_to
                 FROM messages WHERE id=$1 AND deleted_at IS NULL",
            )
            .bind(id)
            .fetch_optional(&self.0)
            .await,
        )?;
        match row {
            None => Ok(None),
            Some(row) => {
                let attach_rows = load_attachment_rows(&self.0, &[id]).await?;
                let attachments = attach_rows
                    .iter()
                    .map(attachment_from)
                    .collect::<Result<Vec<_>, _>>()?;
                let mentions = load_mentions(&self.0, &[id])
                    .await?
                    .remove(&id)
                    .unwrap_or_default();
                Ok(Some(message_from(&row, attachments, mentions)?))
            }
        }
    }

    async fn insert_message(
        &self,
        message: Message,
        member_ids: &[Uuid],
        event: &str,
        payload: Value,
        idempotency_key: Option<&str>,
    ) -> Result<(Message, Vec<OutboxEvent>), StoreError> {
        let mut tx = map_db(self.0.begin().await)?;
        map_db(
            sqlx::query(
                "INSERT INTO messages (id, channel_id, author_id, kind, content, created_at, reply_to)
                 VALUES ($1,$2,$3,$4,$5,$6,$7)",
            )
            .bind(message.id)
            .bind(message.channel_id)
            .bind(message.author_id)
            .bind(&message.kind)
            .bind(&message.content)
            .bind(DateTime::parse_from_rfc3339(&message.created_at)
                .map(|d| d.with_timezone(&Utc))
                .unwrap_or_else(|_| Utc::now()))
            .bind(message.reply_to)
            .execute(&mut *tx)
            .await,
        )?;
        if let Some(key) = idempotency_key {
            map_db(
                sqlx::query(
                    "INSERT INTO idempotency_keys (user_id, key, message_id) VALUES ($1,$2,$3)",
                )
                .bind(message.author_id)
                .bind(key)
                .bind(message.id)
                .execute(&mut *tx)
                .await,
            )?;
        }
        replace_mentions_tx(&mut tx, message.id, &message.mentions).await?;
        let events = enqueue_tx(&mut tx, member_ids, event, &payload).await?;
        map_db(tx.commit().await)?;
        Ok((message, events))
    }

    async fn update_message(
        &self,
        message: Message,
        member_ids: &[Uuid],
        event: &str,
        payload: Value,
    ) -> Result<(Message, Vec<OutboxEvent>), StoreError> {
        let mut tx = map_db(self.0.begin().await)?;
        let result = map_db(
            sqlx::query(
                "UPDATE messages SET content=$1, edited_at=$2 WHERE id=$3 AND deleted_at IS NULL",
            )
            .bind(&message.content)
            .bind(
                message
                    .edited_at
                    .as_deref()
                    .and_then(|value| DateTime::parse_from_rfc3339(value).ok())
                    .map(|value| value.with_timezone(&Utc))
                    .unwrap_or_else(Utc::now),
            )
            .bind(message.id)
            .execute(&mut *tx)
            .await,
        )?;
        if result.rows_affected() == 0 {
            return Err(StoreError::NotFound);
        }
        replace_mentions_tx(&mut tx, message.id, &message.mentions).await?;
        let events = enqueue_tx(&mut tx, member_ids, event, &payload).await?;
        map_db(tx.commit().await)?;
        Ok((message, events))
    }

    async fn page_messages(
        &self,
        channel: Uuid,
        before: Option<Uuid>,
        limit: u32,
    ) -> Result<(Vec<Message>, Option<Uuid>), StoreError> {
        let rows = map_db(
            sqlx::query(
                "SELECT id, channel_id, author_id, kind, content, created_at, edited_at, reply_to
                 FROM messages
                 WHERE channel_id=$1 AND deleted_at IS NULL AND ($2::uuid IS NULL OR id < $2)
                 ORDER BY id DESC LIMIT $3",
            )
            .bind(channel)
            .bind(before)
            .bind((limit + 1) as i64)
            .fetch_all(&self.0)
            .await,
        )?;
        let has_more = rows.len() > limit as usize;
        let page: Vec<PgRow> = rows.into_iter().take(limit as usize).collect();
        let ids: Vec<Uuid> = page
            .iter()
            .map(|row| row.try_get("id").map_err(|_| StoreError::Unavailable))
            .collect::<Result<_, _>>()?;
        let attach_rows = load_attachment_rows(&self.0, &ids).await?;
        let mut grouped: HashMap<Uuid, Vec<Attachment>> = HashMap::new();
        for row in &attach_rows {
            let message_id: Uuid = row
                .try_get("message_id")
                .map_err(|_| StoreError::Unavailable)?;
            grouped
                .entry(message_id)
                .or_default()
                .push(attachment_from(row)?);
        }
        let mut mentioned = load_mentions(&self.0, &ids).await?;
        let messages: Vec<Message> = page
            .iter()
            .map(|row| {
                let id: Uuid = row.try_get("id").map_err(|_| StoreError::Unavailable)?;
                message_from(
                    row,
                    grouped.remove(&id).unwrap_or_default(),
                    mentioned.remove(&id).unwrap_or_default(),
                )
            })
            .collect::<Result<_, _>>()?;
        let next = if has_more {
            messages.last().map(|m| m.id)
        } else {
            None
        };
        Ok((messages, next))
    }

    async fn enqueue(
        &self,
        member_ids: &[Uuid],
        event: &str,
        payload: Value,
    ) -> Result<Vec<OutboxEvent>, StoreError> {
        let mut tx = map_db(self.0.begin().await)?;
        let events = enqueue_tx(&mut tx, member_ids, event, &payload).await?;
        map_db(tx.commit().await)?;
        Ok(events)
    }

    async fn create_gateway_session(
        &self,
        id: Uuid,
        user: Uuid,
        expires_at: i64,
    ) -> Result<GatewaySession, StoreError> {
        map_db(
            sqlx::query(
                "INSERT INTO gateway_sessions (id, user_id, last_seq, expires_at) VALUES ($1,$2,0,$3)",
            )
            .bind(id)
            .bind(user)
            .bind(ts(expires_at))
            .execute(&self.0)
            .await,
        )?;
        Ok(GatewaySession {
            id,
            user_id: user,
            last_seq: 0,
            expires_at,
        })
    }

    async fn find_gateway_session(&self, id: Uuid) -> Result<Option<GatewaySession>, StoreError> {
        let row = map_db(
            sqlx::query(
                "SELECT id, user_id, last_seq, EXTRACT(EPOCH FROM expires_at)::bigint AS exp
                 FROM gateway_sessions WHERE id=$1",
            )
            .bind(id)
            .fetch_optional(&self.0)
            .await,
        )?;
        row.map(|row| {
            Ok(GatewaySession {
                id: row.try_get("id").map_err(|_| StoreError::Unavailable)?,
                user_id: row
                    .try_get("user_id")
                    .map_err(|_| StoreError::Unavailable)?,
                last_seq: row
                    .try_get("last_seq")
                    .map_err(|_| StoreError::Unavailable)?,
                expires_at: row.try_get("exp").map_err(|_| StoreError::Unavailable)?,
            })
        })
        .transpose()
    }

    async fn touch_gateway_session(
        &self,
        id: Uuid,
        last_seq: i64,
        expires_at: i64,
    ) -> Result<(), StoreError> {
        map_db(
            sqlx::query("UPDATE gateway_sessions SET last_seq=$2, expires_at=$3 WHERE id=$1")
                .bind(id)
                .bind(last_seq)
                .bind(ts(expires_at))
                .execute(&self.0)
                .await,
        )
        .map(|_| ())
    }

    async fn replay(
        &self,
        user: Uuid,
        after: i64,
        limit: u32,
    ) -> Result<Vec<OutboxEvent>, StoreError> {
        let rows = map_db(
            sqlx::query(
                "SELECT seq, user_id, event, payload FROM event_outbox
                 WHERE user_id=$1 AND seq>$2 ORDER BY seq LIMIT $3",
            )
            .bind(user)
            .bind(after)
            .bind(limit as i64)
            .fetch_all(&self.0)
            .await,
        )?;
        rows.iter()
            .map(|row| {
                Ok(OutboxEvent {
                    seq: row.try_get("seq").map_err(|_| StoreError::Unavailable)?,
                    user_id: row
                        .try_get("user_id")
                        .map_err(|_| StoreError::Unavailable)?,
                    event: row.try_get("event").map_err(|_| StoreError::Unavailable)?,
                    payload: row
                        .try_get("payload")
                        .map_err(|_| StoreError::Unavailable)?,
                })
            })
            .collect()
    }

    async fn trim_outbox(&self, retain: i64, max_per_user: i64) -> Result<(), StoreError> {
        map_db(
            sqlx::query(
                "DELETE FROM event_outbox WHERE created_at < now() - make_interval(secs => $1)",
            )
            .bind(retain)
            .execute(&self.0)
            .await,
        )?;
        map_db(
            sqlx::query(
                "DELETE FROM event_outbox o
                 WHERE o.seq IN (
                   SELECT seq FROM (
                     SELECT seq, row_number() OVER (PARTITION BY user_id ORDER BY seq DESC) AS n
                     FROM event_outbox
                   ) ranked WHERE n > $1
                 )",
            )
            .bind(max_per_user)
            .execute(&self.0)
            .await,
        )
        .map(|_| ())
    }
}
