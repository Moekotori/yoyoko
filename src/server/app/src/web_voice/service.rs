use crate::{
    error::{ApiErr, ApiResult},
    services::{self, validate_display},
    state::AppState,
};
use argon2::password_hash::{PasswordHasher, SaltString};
use chat_domain::channel::ChannelKind;
use chat_protocol::{User, VoiceGuestRequest, VoiceGuestSession, VoiceRoom};
use rand::{RngCore, rngs::OsRng};
use uuid::Uuid;

pub async fn preview(state: &AppState, channel_id: Uuid) -> ApiResult<VoiceRoom> {
    let channel = state
        .store
        .find_channel(channel_id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    if channel.kind != ChannelKind::Voice {
        return Err(ApiErr::not_found());
    }
    let server_id = channel.server_id.ok_or_else(ApiErr::not_found)?;
    let server = state
        .store
        .find_server(server_id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    Ok(VoiceRoom {
        channel_id: channel.id,
        server_id,
        name: channel.name,
        server_name: server.name,
        kind: channel.kind.as_str().into(),
        participant_count: state.voice.list_channel(channel_id).await.len() as u32,
    })
}

pub async fn join(
    state: &AppState,
    channel_id: Uuid,
    body: VoiceGuestRequest,
    rtc_public_url: String,
) -> ApiResult<VoiceGuestSession> {
    let _ = preview(state, channel_id).await?;
    validate_display(&body.display_name)?;
    let user = create_guest(state, body.display_name.trim()).await?;
    let session_id = Uuid::now_v7();
    let access_token = state
        .tokens
        .issue_voice_access(user.id, session_id, channel_id);
    let joined = services::join_voice(
        state,
        user.id,
        channel_id,
        body.self_mute,
        body.self_deaf,
        None,
        &rtc_public_url,
        Some(channel_id),
    )
    .await?;
    Ok(VoiceGuestSession {
        access_token,
        expires_in: state.tokens.access_ttl_seconds,
        user,
        room: preview(state, channel_id).await?,
        join: joined,
    })
}

async fn create_guest(state: &AppState, display_name: &str) -> ApiResult<User> {
    let salt = SaltString::generate(&mut OsRng);
    let mut secret = [0u8; 24];
    OsRng.fill_bytes(&mut secret);
    let hash = services::argon2()
        .hash_password(hex::encode(secret).as_bytes(), &salt)
        .map_err(|_| ApiErr::unavailable())?
        .to_string();
    for _ in 0..4 {
        let id = Uuid::now_v7();
        let compact = id.simple().to_string();
        let username = format!("w{}", &compact[..12]);
        match state
            .store
            .create_user(id, &username, display_name, &hash)
            .await
        {
            Ok(user) => {
                return Ok(User {
                    id: user.id,
                    username: user.username,
                    display_name: user.display_name,
                    avatar: None,
                    banner: None,
                });
            }
            Err(crate::store::StoreError::Conflict(_)) => continue,
            Err(error) => return Err(error.into()),
        }
    }
    Err(ApiErr::unavailable())
}
