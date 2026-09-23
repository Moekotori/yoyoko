use crate::{
    error::{ApiErr, ApiResult},
    identity::TokenService,
    objects::{display_name, is_animated, sniff_file},
    realtime::Hub,
    state::AppState,
    store::{OutboxEvent, SessionRecord, Store},
};
use argon2::{
    Argon2, Params,
    password_hash::{PasswordHash, PasswordHasher, PasswordVerifier, SaltString},
};
use axum::http::StatusCode;
use chat_domain::{
    channel::{Channel, ChannelKind, Server},
    message::{Attachment, Message},
    permission::Permissions,
    user::User,
};
use chat_protocol::{
    AuthResponse, DEFAULT_PAGE_SIZE, MAX_ATTACHMENTS_PER_MESSAGE, MAX_AVATAR_BYTES,
    MAX_AVATAR_EDGE, MAX_CONTENT_BYTES, MAX_PAGE_SIZE, PatchMeRequest,
};
use chrono::Utc;
use rand::rngs::OsRng;
use std::{collections::HashSet, sync::Arc, time::Duration};
use uuid::Uuid;

pub fn argon2() -> Argon2<'static> {
    Argon2::new(
        argon2::Algorithm::Argon2id,
        argon2::Version::V0x13,
        Params::new(16_384, 2, 1, None).expect("argon2 params"),
    )
}

pub fn invite_code() -> String {
    const ALPH: &[u8] = b"ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    use rand::Rng;
    let mut rng = rand::thread_rng();
    (0..8)
        .map(|_| ALPH[rng.gen_range(0..ALPH.len())] as char)
        .collect()
}

fn to_protocol_media(
    state: &AppState,
    att: &chat_domain::message::Attachment,
    animated: bool,
) -> chat_protocol::Avatar {
    let dto = attachment_dto(&state.tokens, &state.settings.api_origin(), att);
    chat_protocol::Avatar {
        id: dto.id,
        mime_type: dto.mime_type,
        size: dto.size,
        download_url: dto.download_url,
        thumbnail_url: dto.thumbnail_url,
        animated,
    }
}

fn to_protocol_user(state: &AppState, user: &User) -> chat_protocol::User {
    chat_protocol::User {
        id: user.id,
        username: user.username.clone(),
        display_name: user.display_name.clone(),
        avatar: user
            .avatar
            .as_ref()
            .map(|att| to_protocol_media(state, att, user.avatar_animated)),
        banner: user
            .banner
            .as_ref()
            .map(|att| to_protocol_media(state, att, user.banner_animated)),
    }
}

fn to_protocol_server(server: Server) -> chat_protocol::Server {
    chat_protocol::Server {
        id: server.id,
        name: server.name,
        owner_id: server.owner_id,
        invite_code: server.invite_code,
        blocked_words: server.blocked_words,
        cooldown_seconds: server.cooldown_seconds,
    }
}

const MAX_BLOCKED_WORDS: usize = 200;
const MAX_BLOCKED_WORD_LEN: usize = 32;
const MAX_COOLDOWN_SECONDS: u32 = 600;

fn normalize_blocked_words(words: Vec<String>) -> ApiResult<Vec<String>> {
    if words.len() > MAX_BLOCKED_WORDS {
        return Err(ApiErr::bad("too_many_words", "At most 200 blocked words."));
    }
    let mut seen = std::collections::BTreeSet::new();
    let mut out = Vec::new();
    for word in words {
        let trimmed = word.trim();
        if trimmed.is_empty() {
            continue;
        }
        if trimmed.chars().count() > MAX_BLOCKED_WORD_LEN {
            return Err(ApiErr::bad(
                "invalid_word",
                "Blocked words must be 1-32 characters.",
            ));
        }
        let key = trimmed.to_lowercase();
        if seen.insert(key.clone()) {
            out.push(key);
        }
    }
    Ok(out)
}

fn contains_blocked(text: &str, words: &[String]) -> bool {
    if words.is_empty() {
        return false;
    }
    let haystack = text.to_lowercase();
    words.iter().any(|word| haystack.contains(word))
}

pub async fn patch_moderation(
    state: &AppState,
    user: Uuid,
    server_id: Uuid,
    words: Option<Vec<String>>,
    cooldown: Option<u32>,
) -> ApiResult<chat_protocol::Server> {
    let server = state
        .store
        .find_server(server_id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    if server.owner_id != user {
        let channels = state.store.list_channels(server_id).await?;
        let allowed = match channels.first() {
            Some(channel) => state
                .store
                .permissions(user, channel.id)
                .await?
                .resolve()
                .is_some_and(|bits| bits.contains(Permissions::MANAGE_MESSAGES)),
            None => false,
        };
        if !allowed {
            return Err(ApiErr::forbidden());
        }
    }
    if let Some(seconds) = cooldown
        && seconds > MAX_COOLDOWN_SECONDS
    {
        return Err(ApiErr::bad(
            "invalid_cooldown",
            "Cooldown must be 0-600 seconds.",
        ));
    }
    let blocked_words = match words {
        Some(list) => normalize_blocked_words(list)?,
        None => server.blocked_words.clone(),
    };
    let cooldown_seconds = cooldown.unwrap_or(server.cooldown_seconds);
    let updated = state
        .store
        .update_moderation(server_id, blocked_words, cooldown_seconds)
        .await?;
    state.hot.forget_server(server_id).await;
    state.hot.put_server(updated.clone()).await;
    Ok(to_protocol_server(updated))
}

pub(crate) fn to_protocol_channel(channel: Channel) -> chat_protocol::Channel {
    chat_protocol::Channel {
        id: channel.id,
        server_id: channel.server_id,
        name: channel.name,
        kind: channel.kind.as_str().into(),
        audio_quality: if channel.kind == ChannelKind::Voice {
            Some(channel.audio_quality.as_str().into())
        } else {
            None
        },
        participants: if channel.participants.is_empty() {
            None
        } else {
            Some(channel.participants)
        },
    }
}

pub(crate) async fn audience(state: &AppState, channel: &Channel) -> ApiResult<Vec<Uuid>> {
    if channel.is_direct() {
        if channel.participants.len() >= 2 {
            return Ok(channel.participants.clone());
        }
        return Ok(state.store.list_dm_participants(channel.id).await?);
    }
    let server_id = channel.server_id.ok_or_else(ApiErr::unavailable)?;
    Ok(state.store.list_members(server_id).await?)
}

pub(crate) fn parse_quality(value: Option<&str>) -> ApiResult<chat_domain::voice::AudioQuality> {
    match value.map(str::trim).filter(|s| !s.is_empty()) {
        None => Ok(chat_domain::voice::AudioQuality::DEFAULT),
        Some(value) => chat_domain::voice::AudioQuality::parse(value).ok_or_else(|| {
            ApiErr::bad(
                "invalid_audio_quality",
                "audio_quality must be standard, high, very_high, or studio.",
            )
        }),
    }
}

fn audio_profile(quality: chat_domain::voice::AudioQuality) -> chat_protocol::AudioProfile {
    chat_protocol::AudioProfile {
        id: quality.as_str().into(),
        sample_rate_hz: quality.sample_rate_hz(),
        channels: quality.channels(),
        bitrate_bps: quality.bitrate_bps(),
        frame_ms: quality.frame_ms(),
        dtx: quality.dtx(),
        fec: quality.fec(),
    }
}

pub fn attachment_dto(
    tokens: &TokenService,
    api: &str,
    attachment: &Attachment,
) -> chat_protocol::Attachment {
    let exp = Utc::now().timestamp() + 3600;
    let content = tokens.sign_attachment(attachment.id, exp, "content");
    let thumb = attachment.thumbnail_key.map(|_| {
        let sig = tokens.sign_attachment(attachment.id, exp, "thumb");
        format!(
            "{api}/attachments/{}/thumbnail?exp={exp}&sig={sig}",
            attachment.id
        )
    });
    chat_protocol::Attachment {
        id: attachment.id,
        file_name: attachment.file_name.clone(),
        mime_type: attachment.mime_type.clone(),
        size: attachment.size,
        download_url: format!(
            "{api}/attachments/{}/content?exp={exp}&sig={content}",
            attachment.id
        ),
        thumbnail_url: thumb,
    }
}

pub fn message_dto(tokens: &TokenService, api: &str, message: &Message) -> chat_protocol::Message {
    let flags = mention_flags(message.content.as_deref());
    chat_protocol::Message {
        id: message.id,
        channel_id: message.channel_id,
        author_id: message.author_id,
        kind: message.kind.clone(),
        content: message.content.clone(),
        created_at: message.created_at.clone(),
        edited_at: message.edited_at.clone(),
        reply_to: message.reply_to,
        mentions: message.mentions.clone(),
        mention_everyone: flags.0,
        mention_here: flags.1,
        attachments: message
            .attachments
            .iter()
            .map(|item| attachment_dto(tokens, api, item))
            .collect(),
        embeds: vec![],
        reactions: vec![],
        encrypted_payload: None,
    }
}

async fn cached_channel(state: &AppState, id: Uuid) -> ApiResult<Channel> {
    if let Some(channel) = state.hot.channel(id).await {
        return Ok(channel);
    }
    let channel = state
        .store
        .find_channel(id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    state.hot.put_channel(channel.clone()).await;
    Ok(channel)
}

async fn cached_server(state: &AppState, id: Uuid) -> ApiResult<Server> {
    if let Some(server) = state.hot.server(id).await {
        return Ok(server);
    }
    let server = state
        .store
        .find_server(id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    state.hot.put_server(server.clone()).await;
    Ok(server)
}

async fn access(
    state: &AppState,
    user: Uuid,
    channel_id: Uuid,
) -> ApiResult<(Channel, crate::store::PermissionSnapshot)> {
    if let (Some(channel), Some(snapshot)) = (
        state.hot.channel(channel_id).await,
        state.hot.access(user, channel_id).await,
    ) {
        return Ok((channel, snapshot));
    }
    let channel = cached_channel(state, channel_id).await?;
    let snapshot = if let Some(snapshot) = state.hot.access(user, channel_id).await {
        snapshot
    } else {
        let snapshot = state.store.permissions(user, channel_id).await?;
        state
            .hot
            .put_access(user, channel_id, snapshot.clone())
            .await;
        snapshot
    };
    Ok((channel, snapshot))
}

pub(crate) async fn require(
    state: &AppState,
    user: Uuid,
    channel: Uuid,
    permission: Permissions,
) -> ApiResult<Channel> {
    let (channel, snapshot) = access(state, user, channel).await?;
    let resolved = snapshot.resolve().ok_or_else(ApiErr::forbidden)?;
    if !resolved.contains(permission) {
        return Err(ApiErr::forbidden());
    }
    Ok(channel)
}

pub async fn require_view(state: &AppState, user: Uuid, channel: Uuid) -> ApiResult<()> {
    require(state, user, channel, Permissions::VIEW_CHANNEL).await?;
    Ok(())
}

pub async fn register(
    state: &AppState,
    username: String,
    display_name: String,
    password: String,
    server_password: Option<String>,
) -> ApiResult<AuthResponse> {
    if let Some(required) = state.settings.auth.registration_password.as_deref() {
        if server_password.as_deref() != Some(required) {
            return Err(ApiErr::new(
                StatusCode::FORBIDDEN,
                "invalid_server_password",
                "Server password is required or incorrect.",
            ));
        }
    }
    validate_username(&username)?;
    validate_display(&display_name)?;
    validate_password(&password)?;
    let salt = SaltString::generate(&mut OsRng);
    let hash = argon2()
        .hash_password(password.as_bytes(), &salt)
        .map_err(|_| ApiErr::unavailable())?
        .to_string();
    let user = state
        .store
        .create_user(Uuid::now_v7(), &username, &display_name, &hash)
        .await?;
    issue_session(state, user).await
}

pub async fn login(
    state: &AppState,
    username: String,
    password: String,
) -> ApiResult<AuthResponse> {
    let (user, hash) = state
        .store
        .find_user_by_username(&username)
        .await?
        .ok_or_else(|| {
            ApiErr::new(
                StatusCode::UNAUTHORIZED,
                "invalid_credentials",
                "Invalid username or password.",
            )
        })?;
    let parsed = PasswordHash::new(&hash).map_err(|_| ApiErr::unavailable())?;
    argon2()
        .verify_password(password.as_bytes(), &parsed)
        .map_err(|_| {
            ApiErr::new(
                StatusCode::UNAUTHORIZED,
                "invalid_credentials",
                "Invalid username or password.",
            )
        })?;
    issue_session(state, user).await
}

pub async fn refresh(state: &AppState, refresh_token: String) -> ApiResult<AuthResponse> {
    let hash = TokenService::hash_refresh(&refresh_token);
    let session = state
        .store
        .find_session_by_refresh(hash)
        .await?
        .ok_or_else(ApiErr::unauthorized)?;
    if session.revoked || session.expires_at < Utc::now().timestamp() {
        return Err(ApiErr::unauthorized());
    }
    let user = state
        .store
        .find_user(session.user_id)
        .await?
        .ok_or_else(ApiErr::unauthorized)?;
    let (token, new_hash) = TokenService::random_refresh();
    let expires = Utc::now().timestamp() + state.tokens.refresh_ttl_days as i64 * 86400;
    state
        .store
        .rotate_session(session.id, new_hash, expires)
        .await?;
    Ok(AuthResponse {
        access_token: state.tokens.issue_access(user.id, session.id),
        refresh_token: token,
        expires_in: state.tokens.access_ttl_seconds,
        user: to_protocol_user(state, &user),
    })
}

pub async fn me(state: &AppState, user: Uuid) -> ApiResult<chat_protocol::User> {
    let user = state
        .store
        .find_user(user)
        .await?
        .ok_or_else(ApiErr::unauthorized)?;
    Ok(to_protocol_user(state, &user))
}

pub async fn patch_me(
    state: &AppState,
    user_id: Uuid,
    patch: PatchMeRequest,
) -> ApiResult<chat_protocol::User> {
    if patch.avatar_id.is_some() && patch.clear_avatar {
        return Err(ApiErr::bad(
            "invalid_profile",
            "Cannot set and clear avatar together.",
        ));
    }
    if patch.banner_id.is_some() && patch.clear_banner {
        return Err(ApiErr::bad(
            "invalid_profile",
            "Cannot set and clear banner together.",
        ));
    }
    if !state
        .limiter
        .check(&format!("profile:{user_id}"), 20, Duration::from_secs(3600))
        .await
    {
        return Err(ApiErr::too_many());
    }
    let current = state
        .store
        .find_user(user_id)
        .await?
        .ok_or_else(ApiErr::unauthorized)?;
    let username = match patch.username {
        Some(value) => {
            validate_username(&value)?;
            value
        }
        None => current.username.clone(),
    };
    let display_name = match patch.display_name {
        Some(value) => {
            let trimmed = value.trim().to_string();
            validate_display(&trimmed)?;
            trimmed
        }
        None => current.display_name.clone(),
    };
    let (avatar_id, avatar_animated) = resolve_profile_image(
        state,
        user_id,
        patch.clear_avatar,
        patch.avatar_id,
        current.avatar.as_ref().map(|att| att.id),
        current.avatar_animated,
        "avatar",
        "Avatar",
    )
    .await?;
    let (banner_id, banner_animated) = resolve_profile_image(
        state,
        user_id,
        patch.clear_banner,
        patch.banner_id,
        current.banner.as_ref().map(|att| att.id),
        current.banner_animated,
        "banner",
        "Banner",
    )
    .await?;
    let updated = state
        .store
        .update_user(
            user_id,
            &username,
            &display_name,
            avatar_id,
            avatar_animated,
            banner_id,
            banner_animated,
        )
        .await?;
    if let Some(mut voice) = state.voice.get(user_id).await
        && voice.display_name != updated.display_name
    {
        voice.display_name = updated.display_name.clone();
        state.voice.put(voice.clone()).await;
        publish_voice(state, &voice, false).await?;
    }
    let dto = to_protocol_user(state, &updated);
    let payload = serde_json::to_value(&dto).unwrap_or_default();
    let members: Vec<Uuid> = state
        .store
        .list_visible_users(user_id)
        .await?
        .into_iter()
        .map(|user| user.id)
        .collect();
    state.hot.forget_user(user_id).await;
    dispatch(state, &members, "USER_UPDATE", payload).await?;
    Ok(dto)
}

async fn resolve_profile_image(
    state: &AppState,
    user_id: Uuid,
    clear: bool,
    next_id: Option<Uuid>,
    current_id: Option<Uuid>,
    current_animated: bool,
    code: &str,
    label: &str,
) -> ApiResult<(Option<Uuid>, bool)> {
    if clear {
        return Ok((None, false));
    }
    let Some(id) = next_id else {
        return Ok((current_id, current_animated));
    };
    let (att, uploader, message_id) = state
        .store
        .get_attachment(id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    if uploader != user_id || message_id.is_some() {
        return Err(ApiErr::bad(
            profile_error_code(code),
            format!("{label} must be an unused image you uploaded."),
        ));
    }
    if att.size > MAX_AVATAR_BYTES {
        return Err(ApiErr::bad(
            "too_large",
            format!("{label} exceeds 8 MiB."),
        ));
    }
    if !att.mime_type.starts_with("image/") || att.mime_type == "image/svg+xml" {
        return Err(ApiErr::bad(
            profile_error_code(code),
            format!("{label} must be a JPEG, PNG, GIF, or WebP image."),
        ));
    }
    let path = state.objects.path(att.object_key);
    if let Ok((width, height)) = image::image_dimensions(&path)
        && (width > MAX_AVATAR_EDGE || height > MAX_AVATAR_EDGE)
    {
        return Err(ApiErr::bad(
            profile_error_code(code),
            format!("{label} must be 4096px or smaller."),
        ));
    }
    Ok((Some(id), is_animated(&path, &att.mime_type)))
}

pub async fn logout(state: &AppState, user: Uuid, session_id: Uuid) -> ApiResult<()> {
    let _ = leave_voice(state, user).await;
    state.store.revoke_session(session_id).await?;
    Ok(())
}

async fn issue_session(state: &AppState, user: User) -> ApiResult<AuthResponse> {
    let session_id = Uuid::now_v7();
    let (refresh_token, hash) = TokenService::random_refresh();
    let expires = Utc::now().timestamp() + state.tokens.refresh_ttl_days as i64 * 86400;
    state
        .store
        .create_session(SessionRecord {
            id: session_id,
            user_id: user.id,
            refresh_hash: hash,
            expires_at: expires,
            revoked: false,
        })
        .await?;
    Ok(AuthResponse {
        access_token: state.tokens.issue_access(user.id, session_id),
        refresh_token,
        expires_in: state.tokens.access_ttl_seconds,
        user: to_protocol_user(state, &user),
    })
}

pub async fn create_server(
    state: &AppState,
    user: Uuid,
    name: String,
) -> ApiResult<chat_protocol::Server> {
    validate_display(&name)?;
    let server = Server {
        id: Uuid::now_v7(),
        name,
        owner_id: user,
        invite_code: invite_code(),
        blocked_words: vec![],
        cooldown_seconds: 0,
    };
    let channel = Channel::in_server(
        Uuid::now_v7(),
        server.id,
        "general",
        ChannelKind::Text,
        chat_domain::voice::AudioQuality::Standard,
    );
    let created = state
        .store
        .create_server(user, server, Uuid::now_v7(), Uuid::now_v7(), channel)
        .await?;
    state.hot.forget_user(user).await;
    state.hot.put_server(created.server.clone()).await;
    state.hot.put_channel(created.channel.clone()).await;
    let voice = state
        .store
        .add_channel(Channel::in_server(
            Uuid::now_v7(),
            created.server.id,
            "语音",
            ChannelKind::Voice,
            chat_domain::voice::AudioQuality::DEFAULT,
        ))
        .await?;
    state.hot.put_channel(voice).await;
    Ok(to_protocol_server(created.server))
}

pub async fn list_servers(state: &AppState, user: Uuid) -> ApiResult<Vec<chat_protocol::Server>> {
    Ok(state
        .store
        .list_servers(user)
        .await?
        .into_iter()
        .map(to_protocol_server)
        .collect())
}

pub async fn list_channels(
    state: &AppState,
    user: Uuid,
    server_id: Uuid,
) -> ApiResult<Vec<chat_protocol::Channel>> {
    let servers = state.store.list_servers(user).await?;
    if !servers.iter().any(|s| s.id == server_id) {
        return Err(ApiErr::forbidden());
    }
    Ok(state
        .store
        .list_channels(server_id)
        .await?
        .into_iter()
        .map(to_protocol_channel)
        .collect())
}

pub async fn create_channel(
    state: &AppState,
    user: Uuid,
    server_id: Uuid,
    name: String,
    kind: String,
    audio_quality: Option<String>,
) -> ApiResult<chat_protocol::Channel> {
    validate_display(&name)?;
    let kind = ChannelKind::parse(&kind)
        .filter(|kind| *kind != ChannelKind::Direct)
        .ok_or_else(|| ApiErr::bad("invalid_kind", "kind must be text or voice."))?;
    let servers = state.store.list_servers(user).await?;
    let server = servers
        .iter()
        .find(|s| s.id == server_id)
        .ok_or_else(ApiErr::forbidden)?;
    if server.owner_id != user {
        return Err(ApiErr::forbidden());
    }
    let channel = state
        .store
        .add_channel(Channel::in_server(
            Uuid::now_v7(),
            server_id,
            name,
            kind,
            if kind == ChannelKind::Voice {
                parse_quality(audio_quality.as_deref())?
            } else {
                chat_domain::voice::AudioQuality::Standard
            },
        ))
        .await?;
    state.hot.put_channel(channel.clone()).await;
    let members = state.store.list_members(server_id).await?;
    let payload = serde_json::to_value(to_protocol_channel(channel.clone())).unwrap_or_default();
    dispatch(state, &members, "CHANNEL_CREATE", payload).await?;
    Ok(to_protocol_channel(channel))
}

pub async fn join_server(
    state: &AppState,
    user: Uuid,
    code: String,
) -> ApiResult<chat_protocol::Server> {
    let code = code.trim().to_ascii_uppercase();
    if code.len() < 6 || code.len() > 16 {
        return Err(ApiErr::bad("invalid_invite", "Invalid invite code."));
    }
    let server = state.store.join_invite(user, &code).await?;
    state.hot.forget_user(user).await;
    state.hot.forget_server(server.id).await;
    state.hot.put_server(server.clone()).await;
    let members = state.store.list_members(server.id).await?;
    let user_row = state
        .store
        .find_user(user)
        .await?
        .ok_or_else(ApiErr::unavailable)?;
    let payload = serde_json::json!({
        "server_id": server.id,
        "user": to_protocol_user(state, &user_row)
    });
    dispatch(state, &members, "MEMBER_JOIN", payload).await?;
    Ok(to_protocol_server(server))
}

pub async fn page_messages(
    state: &AppState,
    user: Uuid,
    channel_id: Uuid,
    before: Option<Uuid>,
    limit: Option<u32>,
) -> ApiResult<chat_protocol::MessagePage> {
    require(state, user, channel_id, Permissions::VIEW_CHANNEL).await?;
    let limit = limit.unwrap_or(DEFAULT_PAGE_SIZE).clamp(1, MAX_PAGE_SIZE);
    let (items, before) = state.store.page_messages(channel_id, before, limit).await?;
    let api = state.settings.api_origin();
    Ok(chat_protocol::MessagePage {
        items: items
            .iter()
            .map(|item| message_dto(&state.tokens, &api, item))
            .collect(),
        before,
    })
}

pub async fn send_message(
    state: &AppState,
    user: Uuid,
    channel_id: Uuid,
    content: Option<String>,
    reply_to: Option<Uuid>,
    attachment_ids: Vec<Uuid>,
    idempotency: Option<String>,
) -> ApiResult<chat_protocol::Message> {
    let channel = require(state, user, channel_id, Permissions::SEND_MESSAGE).await?;
    let server = if let Some(server_id) = channel.server_id {
        Some(cached_server(state, server_id).await?)
    } else {
        None
    };
    if let Some(key) = idempotency.as_deref() {
        if !(8..=128).contains(&key.len()) {
            return Err(ApiErr::bad(
                "invalid_idempotency",
                "Idempotency-Key must be 8-128 characters.",
            ));
        }
        if let Some(existing) = state.store.find_idempotent(user, key).await? {
            return Ok(message_dto(
                &state.tokens,
                &state.settings.api_origin(),
                &existing,
            ));
        }
    }
    let content = content.and_then(|value| {
        let trimmed = value.trim().to_string();
        if trimmed.is_empty() {
            None
        } else {
            Some(trimmed)
        }
    });
    if let Some(text) = &content
        && text.len() > MAX_CONTENT_BYTES
    {
        return Err(ApiErr::bad("too_long", "Message is too long."));
    }
    if attachment_ids.len() > MAX_ATTACHMENTS_PER_MESSAGE {
        return Err(ApiErr::bad(
            "too_many_files",
            "At most 4 files per message.",
        ));
    }
    if content.is_none() && attachment_ids.is_empty() {
        return Err(ApiErr::bad(
            "empty_message",
            "Message needs text or a file.",
        ));
    }
    if let Some(parent_id) = reply_to {
        let parent = state
            .store
            .get_message(parent_id)
            .await?
            .ok_or_else(|| {
                ApiErr::bad("invalid_reply", "Reply target was not found in this channel.")
            })?;
        if parent.channel_id != channel_id {
            return Err(ApiErr::bad(
                "invalid_reply",
                "Reply must target a message in this channel.",
            ));
        }
    }
    if let Some(text) = &content
        && let Some(server) = &server
        && contains_blocked(text, &server.blocked_words)
    {
        return Err(ApiErr::blocked_word());
    }
    if let Some(server) = &server
        && server.cooldown_seconds > 0
        && let Some(last) = state.store.last_user_message_at(channel_id, user).await?
    {
        let elapsed = Utc::now().timestamp().saturating_sub(last);
        if elapsed < server.cooldown_seconds as i64 {
            return Err(ApiErr::cooldown(
                (server.cooldown_seconds as i64 - elapsed) as u64,
            ));
        }
    }
    let mentions =
        resolve_channel_mentions(state, &channel, user, content.as_deref(), reply_to).await?;
    let id = Uuid::now_v7();
    let attachments = if attachment_ids.is_empty() {
        vec![]
    } else {
        state
            .store
            .bind_attachments(id, user, &attachment_ids)
            .await?
    };
    let message = Message {
        id,
        channel_id,
        author_id: user,
        kind: "text".into(),
        content,
        created_at: Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true),
        edited_at: None,
        reply_to,
        mentions,
        attachments,
    };
    let api = state.settings.api_origin();
    let dto = message_dto(&state.tokens, &api, &message);
    let payload = serde_json::to_value(&dto).unwrap_or_default();
    let members = audience(state, &channel).await?;
    let (_, events) = state
        .store
        .insert_message(
            message,
            &members,
            "MESSAGE_CREATE",
            payload.clone(),
            idempotency.as_deref(),
        )
        .await?;
    fanout(&state.hub, events).await;
    Ok(dto)
}

pub async fn edit_message(
    state: &AppState,
    user: Uuid,
    channel_id: Uuid,
    message_id: Uuid,
    content: Option<String>,
) -> ApiResult<chat_protocol::Message> {
    let channel = require(state, user, channel_id, Permissions::VIEW_CHANNEL).await?;
    let server = if let Some(server_id) = channel.server_id {
        Some(cached_server(state, server_id).await?)
    } else {
        None
    };
    let mut message = state
        .store
        .get_message(message_id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    if message.channel_id != channel_id {
        return Err(ApiErr::not_found());
    }
    if message.author_id != user {
        return Err(ApiErr::forbidden());
    }
    let content = content.and_then(|value| {
        let trimmed = value.trim().to_string();
        if trimmed.is_empty() {
            None
        } else {
            Some(trimmed)
        }
    });
    if let Some(text) = &content
        && text.len() > MAX_CONTENT_BYTES
    {
        return Err(ApiErr::bad("too_long", "Message is too long."));
    }
    if content.is_none() && message.attachments.is_empty() {
        return Err(ApiErr::bad(
            "empty_message",
            "Message needs text or a file.",
        ));
    }
    if let Some(text) = &content
        && let Some(server) = &server
        && contains_blocked(text, &server.blocked_words)
    {
        return Err(ApiErr::blocked_word());
    }
    message.content = content;
    message.edited_at = Some(Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true));
    message.mentions = resolve_channel_mentions(
        state,
        &channel,
        user,
        message.content.as_deref(),
        message.reply_to,
    )
    .await?;
    let api = state.settings.api_origin();
    let dto = message_dto(&state.tokens, &api, &message);
    let payload = serde_json::to_value(&dto).unwrap_or_default();
    let members = audience(state, &channel).await?;
    let (_, events) = state
        .store
        .update_message(message, &members, "MESSAGE_UPDATE", payload)
        .await?;
    fanout(&state.hub, events).await;
    Ok(dto)
}

pub async fn delete_message(
    state: &AppState,
    user: Uuid,
    channel_id: Uuid,
    message_id: Uuid,
) -> ApiResult<()> {
    let channel = require(state, user, channel_id, Permissions::VIEW_CHANNEL).await?;
    let message = state
        .store
        .get_message(message_id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    if message.channel_id != channel_id {
        return Err(ApiErr::not_found());
    }
    let can_manage = state
        .store
        .permissions(user, channel_id)
        .await?
        .resolve()
        .is_some_and(|bits| bits.contains(Permissions::MANAGE_MESSAGES));
    if message.author_id != user && !can_manage {
        return Err(ApiErr::forbidden());
    }
    let payload = serde_json::json!({
        "id": message.id,
        "channel_id": message.channel_id,
    });
    let members = audience(state, &channel).await?;
    let events = state
        .store
        .delete_message(message.id, &members, "MESSAGE_DELETE", payload)
        .await?;
    fanout(&state.hub, events).await;
    Ok(())
}

pub async fn start_typing(state: &AppState, user: Uuid, channel_id: Uuid) -> ApiResult<()> {
    let channel = require(state, user, channel_id, Permissions::SEND_MESSAGE).await?;
    let user_row = state
        .store
        .find_user(user)
        .await?
        .ok_or_else(ApiErr::unavailable)?;
    let members = audience(state, &channel).await?;
    let envelope = chat_protocol::GatewayEnvelope {
        op: "dispatch".into(),
        event: Some("TYPING_START".into()),
        seq: None,
        data: serde_json::json!({
            "user_id": user,
            "channel_id": channel_id,
            "display_name": user_row.display_name,
        }),
    };
    for member in members {
        if member != user {
            state.hub.send(member, envelope.clone()).await;
        }
    }
    Ok(())
}

pub async fn upload(
    state: &AppState,
    user: Uuid,
    file_name: String,
    mime: String,
    tmp: std::path::PathBuf,
    size: u64,
) -> ApiResult<chat_protocol::Attachment> {
    let sniffed = match sniff_file(&tmp, &mime, &file_name) {
        Ok(value) => value,
        Err(err) => {
            let _ = tokio::fs::remove_file(&tmp).await;
            return Err(err);
        }
    };
    let id = Uuid::now_v7();
    let object_key = Uuid::now_v7();
    state.objects.commit(&tmp, object_key).await?;
    let thumbnail_key = if sniffed.raster {
        state
            .objects
            .thumbnail(object_key)
            .await
            .map(|_| object_key)
    } else {
        None
    };
    let mime = sniffed.mime.to_string();
    let attachment = Attachment {
        id,
        file_name: display_name(&file_name),
        mime_type: mime,
        size,
        object_key,
        thumbnail_key,
    };
    state
        .store
        .insert_attachment(attachment.clone(), user)
        .await?;
    Ok(attachment_dto(
        &state.tokens,
        &state.settings.api_origin(),
        &attachment,
    ))
}

pub async fn ready_payload(
    state: &AppState,
    user: Uuid,
    session_id: Uuid,
) -> ApiResult<chat_protocol::Ready> {
    let me = state
        .store
        .find_user(user)
        .await?
        .ok_or_else(ApiErr::unauthorized)?;
    let servers = state.store.list_servers(user).await?;
    let channels = state.store.list_channels_for_user(user).await?;
    let users = state.store.list_visible_users(user).await?;
    let server_ids: Vec<Uuid> = servers.iter().map(|server| server.id).collect();
    let voice_states = state
        .voice
        .for_servers(&server_ids)
        .await
        .into_iter()
        .map(to_protocol_voice)
        .collect();
    Ok(chat_protocol::Ready {
        session_id: session_id.to_string(),
        user: to_protocol_user(state, &me),
        servers: servers.into_iter().map(to_protocol_server).collect(),
        channels: channels.into_iter().map(to_protocol_channel).collect(),
        users: users
            .iter()
            .map(|user| to_protocol_user(state, user))
            .collect(),
        heartbeat_interval_ms: 30_000,
        voice_states,
    })
}

fn to_protocol_voice(state: chat_domain::voice::VoiceState) -> chat_protocol::VoiceState {
    chat_protocol::VoiceState {
        user_id: state.user_id,
        server_id: state.server_id,
        channel_id: Some(state.channel_id),
        self_mute: state.self_mute,
        self_deaf: state.self_deaf,
        display_name: state.display_name,
        audio_quality: state.audio_quality.as_str().into(),
    }
}

pub async fn join_voice(
    state: &AppState,
    user: Uuid,
    channel_id: Uuid,
    mut self_mute: bool,
    self_deaf: bool,
    requested_quality: Option<&str>,
    rtc_public_url: &str,
    scoped: Option<Uuid>,
) -> ApiResult<chat_protocol::VoiceJoin> {
    if self_deaf {
        self_mute = true;
    }
    if scoped.is_some_and(|allowed| allowed != channel_id) {
        return Err(ApiErr::forbidden());
    }
    let channel = if scoped == Some(channel_id) {
        let channel = cached_channel(state, channel_id).await?;
        if channel.kind != ChannelKind::Voice {
            return Err(ApiErr::bad("invalid_channel", "Not a voice channel."));
        }
        channel
    } else {
        require(state, user, channel_id, Permissions::CONNECT_VOICE).await?
    };
    if channel.kind != ChannelKind::Voice {
        return Err(ApiErr::bad("invalid_channel", "Not a voice channel."));
    }
    let can_speak = scoped == Some(channel_id)
        || require(state, user, channel_id, Permissions::SPEAK)
            .await
            .is_ok();
    let display_name = state
        .store
        .find_user(user)
        .await?
        .map(|row| row.display_name)
        .unwrap_or_else(|| user.to_string());
    let previous = state.voice.get(user).await;
    let requested = match requested_quality {
        Some(value) => parse_quality(Some(value))?,
        None => previous
            .as_ref()
            .filter(|prev| prev.channel_id == channel_id)
            .map(|prev| prev.audio_quality)
            .unwrap_or(chat_domain::voice::AudioQuality::DEFAULT),
    };
    let quality = requested.clamp(channel.audio_quality);
    let voice = chat_domain::voice::VoiceState {
        user_id: user,
        server_id: channel.server_id.ok_or_else(ApiErr::not_found)?,
        channel_id,
        self_mute,
        self_deaf,
        display_name,
        audio_quality: quality,
    };
    let unchanged = previous.as_ref() == Some(&voice);
    state.voice.put(voice.clone()).await;
    if let Some(prev) = previous.filter(|prev| prev.channel_id != channel_id) {
        publish_voice(state, &prev, true).await?;
    }
    if !unchanged {
        publish_voice(state, &voice, false).await?;
    }
    let token = crate::rtc::mint_voice_token(
        &state.settings.rtc,
        rtc_public_url,
        &user.to_string(),
        &voice.display_name,
        &chat_domain::voice::VoiceState::room_name(channel_id),
        can_speak,
        quality,
        std::time::Duration::from_secs(300),
    )
    .map_err(|_| ApiErr::unavailable())?;
    Ok(chat_protocol::VoiceJoin {
        rtc: chat_protocol::RtcToken {
            token: token.jwt,
            url: token.url,
            room: token.room,
            expires_at: crate::rtc::rfc3339_unix(token.expires_at_unix),
        },
        state: to_protocol_voice(voice.clone()),
        audio: audio_profile(voice.audio_quality),
        max_audio_quality: channel.audio_quality.as_str().into(),
    })
}

pub async fn patch_voice(
    state: &AppState,
    user: Uuid,
    mut self_mute: bool,
    self_deaf: bool,
    requested_quality: Option<&str>,
) -> ApiResult<chat_protocol::VoiceState> {
    if self_deaf {
        self_mute = true;
    }
    let current = state
        .voice
        .get(user)
        .await
        .ok_or_else(|| ApiErr::conflict("Not connected to a voice channel."))?;
    let channel = cached_channel(state, current.channel_id).await?;
    let quality = match requested_quality {
        Some(value) => parse_quality(Some(value))?.clamp(channel.audio_quality),
        None => current.audio_quality.clamp(channel.audio_quality),
    };
    let voice = chat_domain::voice::VoiceState {
        user_id: current.user_id,
        server_id: current.server_id,
        channel_id: current.channel_id,
        self_mute,
        self_deaf,
        display_name: current.display_name.clone(),
        audio_quality: quality,
    };
    if voice != current {
        state.voice.put(voice.clone()).await;
        publish_voice(state, &voice, false).await?;
    }
    Ok(to_protocol_voice(voice))
}

pub async fn leave_voice(
    state: &AppState,
    user: Uuid,
) -> ApiResult<Option<chat_protocol::VoiceState>> {
    let Some(state_row) = state.voice.remove(user).await else {
        return Ok(None);
    };
    publish_voice(state, &state_row, true).await?;
    Ok(Some(to_protocol_voice(state_row)))
}

pub async fn list_voice(
    state: &AppState,
    user: Uuid,
    channel_id: Uuid,
    scoped: Option<Uuid>,
) -> ApiResult<Vec<chat_protocol::VoiceState>> {
    if scoped == Some(channel_id) {
        let channel = cached_channel(state, channel_id).await?;
        if channel.kind != ChannelKind::Voice {
            return Err(ApiErr::not_found());
        }
    } else if scoped.is_some() {
        return Err(ApiErr::forbidden());
    } else {
        require(state, user, channel_id, Permissions::VIEW_CHANNEL).await?;
    }
    Ok(state
        .voice
        .list_channel(channel_id)
        .await
        .into_iter()
        .map(to_protocol_voice)
        .collect())
}

pub(crate) async fn publish_voice(
    state: &AppState,
    voice: &chat_domain::voice::VoiceState,
    left: bool,
) -> ApiResult<()> {
    let members = state.store.list_members(voice.server_id).await?;
    let payload = serde_json::to_value(chat_protocol::VoiceState {
        user_id: voice.user_id,
        server_id: voice.server_id,
        channel_id: if left { None } else { Some(voice.channel_id) },
        self_mute: voice.self_mute,
        self_deaf: voice.self_deaf,
        display_name: voice.display_name.clone(),
        audio_quality: voice.audio_quality.as_str().into(),
    })
    .unwrap_or_default();
    dispatch(state, &members, "VOICE_STATE_UPDATE", payload).await
}

pub(crate) async fn dispatch(
    state: &AppState,
    members: &[Uuid],
    event: &str,
    payload: serde_json::Value,
) -> ApiResult<()> {
    let events = state.store.enqueue(members, event, payload).await?;
    fanout(&state.hub, events).await;
    Ok(())
}

pub async fn fanout(hub: &Hub, events: Vec<OutboxEvent>) {
    for event in events {
        hub.send(
            event.user_id,
            chat_protocol::GatewayEnvelope {
                op: "dispatch".into(),
                event: Some(event.event),
                seq: Some(event.seq.to_string()),
                data: event.payload,
            },
        )
        .await;
    }
}

const MAX_HERE_MENTIONS: usize = 256;

async fn mention_directory(
    state: &AppState,
    channel: &Channel,
) -> ApiResult<Vec<(Uuid, String, String)>> {
    if let Some(server_id) = channel.server_id {
        if let Some(names) = state.hot.names(server_id).await {
            return Ok(names);
        }
        let names = state.store.list_member_usernames(server_id).await?;
        state.hot.put_names(server_id, names.clone()).await;
        return Ok(names);
    }
    let mut names = Vec::new();
    for id in audience(state, channel).await? {
        if let Some(user) = state.store.find_user(id).await? {
            names.push((user.id, user.username, user.display_name));
        }
    }
    Ok(names)
}

async fn resolve_channel_mentions(
    state: &AppState,
    channel: &Channel,
    author: Uuid,
    content: Option<&str>,
    reply_to: Option<Uuid>,
) -> ApiResult<Vec<Uuid>> {
    let mut found = if let Some(text) = content {
        let names = mention_directory(state, channel).await?;
        let scan = parse_mentions(text, &names);
        let mut users = scan.users;
        if scan.here {
            let connected: HashSet<Uuid> = state.hub.connected_ids().await.into_iter().collect();
            let mut extra = 0;
            for (id, _, _) in &names {
                if extra >= MAX_HERE_MENTIONS {
                    break;
                }
                if *id == author || users.contains(id) || !connected.contains(id) {
                    continue;
                }
                users.push(*id);
                extra += 1;
            }
        }
        users
    } else {
        Vec::new()
    };
    if let Some(reply) = reply_to
        && let Some(parent) = state.store.get_message(reply).await?
        && parent.author_id != author
        && !found.contains(&parent.author_id)
    {
        found.push(parent.author_id);
    }
    Ok(found)
}

fn mention_flags(content: Option<&str>) -> (bool, bool) {
    content
        .map(|text| {
            let scan = parse_mentions(text, &[]);
            (scan.everyone, scan.here)
        })
        .unwrap_or((false, false))
}

struct MentionScan {
    users: Vec<Uuid>,
    everyone: bool,
    here: bool,
}

fn parse_mentions(text: &str, members: &[(Uuid, String, String)]) -> MentionScan {
    let chars: Vec<(usize, char)> = text.char_indices().collect();
    let mut found = Vec::new();
    let mut everyone = false;
    let mut here = false;
    let mut i = 0;
    while i < chars.len() {
        let (byte, c) = chars[i];
        let prev_ok = i == 0 || !is_mention_char(chars[i - 1].1);
        if c == '@' && prev_ok {
            let start = byte + c.len_utf8();
            let mut j = i + 1;
            while j < chars.len() && is_mention_char(chars[j].1) {
                j += 1;
            }
            let end = if j > i + 1 {
                let (last_byte, last_c) = chars[j - 1];
                last_byte + last_c.len_utf8()
            } else {
                start
            };
            if end > start {
                let name = &text[start..end];
                if name.eq_ignore_ascii_case("everyone") {
                    everyone = true;
                } else if name.eq_ignore_ascii_case("here") {
                    here = true;
                } else if let Some((id, _, _)) = members.iter().find(|(_, username, display)| {
                    username.eq_ignore_ascii_case(name) || display.eq_ignore_ascii_case(name)
                }) && !found.contains(id)
                {
                    found.push(*id);
                }
            }
            i = j.max(i + 1);
            continue;
        }
        i += 1;
    }
    MentionScan {
        users: found,
        everyone,
        here,
    }
}

fn is_mention_char(value: char) -> bool {
    value.is_alphanumeric() || value == '_' || value == '-'
}

fn profile_error_code(code: &str) -> &'static str {
    if code == "banner" {
        "invalid_banner"
    } else {
        "invalid_avatar"
    }
}

fn validate_username(value: &str) -> ApiResult<()> {
    if value.eq_ignore_ascii_case("everyone") || value.eq_ignore_ascii_case("here") {
        return Err(ApiErr::bad(
            "invalid_username",
            "Username is reserved.",
        ));
    }
    if (2..=64).contains(&value.len())
        && value
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-')
    {
        Ok(())
    } else {
        Err(ApiErr::bad(
            "invalid_username",
            "Username must be 2-64 letters, numbers, _ or -.",
        ))
    }
}

pub(crate) fn validate_display(value: &str) -> ApiResult<()> {
    let trimmed = value.trim();
    if (1..=100).contains(&trimmed.len()) {
        Ok(())
    } else {
        Err(ApiErr::bad(
            "invalid_name",
            "Name must be 1-100 characters.",
        ))
    }
}

fn validate_password(value: &str) -> ApiResult<()> {
    if (8..=128).contains(&value.len()) {
        Ok(())
    } else {
        Err(ApiErr::bad(
            "invalid_password",
            "Password must be 8-128 characters.",
        ))
    }
}

pub async fn trim_loop(store: Arc<dyn Store>) {
    loop {
        tokio::time::sleep(std::time::Duration::from_secs(60)).await;
        let _ = store.trim_outbox(15 * 60, 10_000).await;
    }
}

#[cfg(test)]
mod mention_tests {
    use super::*;

    #[test]
    fn parse_mentions_users_everyone_here_and_display() {
        let bob = Uuid::now_v7();
        let lin = Uuid::now_v7();
        let scan = parse_mentions(
            "hi @bob @everyone @here @林",
            &[
                (bob, "bob".into(), "Bob".into()),
                (lin, "lin".into(), "林".into()),
            ],
        );
        assert!(scan.everyone);
        assert!(scan.here);
        assert_eq!(scan.users, vec![bob, lin]);
        let email = parse_mentions(
            "mail bob@example.com",
            &[(bob, "bob".into(), "Bob".into())],
        );
        assert!(!email.everyone && !email.here);
        assert!(email.users.is_empty());
    }
}
