use crate::{
    configuration,
    error::{ApiErr, ApiResult},
    gateway,
    services,
    state::AppState,
};
use axum::{
    Json, Router,
    body::Body,
    extract::{DefaultBodyLimit, FromRequestParts, Multipart, Path, Query, State},
    http::{HeaderMap, HeaderName, HeaderValue, Method, StatusCode, header, request::Parts},
    response::{IntoResponse, Response},
    routing::{get, patch, post},
};
use chat_protocol::{
    API_VERSION, ApiError, AuthResponse, Channel, CreateChannelRequest, CreateServerRequest,
    InstanceDiscovery, JoinRequest, LoginRequest, MAX_ATTACHMENTS_PER_MESSAGE, Message,
    MessagePage, PROTOCOL_VERSION, PatchMeRequest, PatchMessageRequest, PatchModerationRequest,
    RefreshRequest, RegisterRequest, SendMessageRequest, Server, User, VoiceFlags, VoiceJoin,
    VoiceState,
};
use serde::Deserialize;
use std::{net::SocketAddr, sync::Arc, time::Duration};
use tokio_util::io::ReaderStream;
use tower_http::{
    cors::{AllowOrigin, CorsLayer},
    limit::RequestBodyLimitLayer,
    timeout::TimeoutLayer,
    trace::TraceLayer,
};
use uuid::Uuid;

pub struct Auth(pub Uuid, pub Uuid);

impl FromRequestParts<Arc<AppState>> for Auth {
    type Rejection = ApiErr;
    async fn from_request_parts(
        parts: &mut Parts,
        state: &Arc<AppState>,
    ) -> Result<Self, Self::Rejection> {
        let header = parts
            .headers
            .get(header::AUTHORIZATION)
            .and_then(|value| value.to_str().ok())
            .ok_or_else(ApiErr::unauthorized)?;
        let token = header
            .strip_prefix("Bearer ")
            .ok_or_else(ApiErr::unauthorized)?;
        let claims = state
            .tokens
            .verify_access(token)
            .ok_or_else(ApiErr::unauthorized)?;
        if claims.voice_channel.is_some() {
            return Err(ApiErr::forbidden());
        }
        Ok(Auth(claims.user_id, claims.session_id))
    }
}

pub struct VoiceAuth {
    pub user: Uuid,
    pub voice_only: Option<Uuid>,
}

impl VoiceAuth {
    pub fn allow(&self, channel: Uuid) -> ApiResult<()> {
        match self.voice_only {
            None => Ok(()),
            Some(allowed) if allowed == channel => Ok(()),
            Some(_) => Err(ApiErr::forbidden()),
        }
    }
}

impl FromRequestParts<Arc<AppState>> for VoiceAuth {
    type Rejection = ApiErr;
    async fn from_request_parts(
        parts: &mut Parts,
        state: &Arc<AppState>,
    ) -> Result<Self, Self::Rejection> {
        let header = parts
            .headers
            .get(header::AUTHORIZATION)
            .and_then(|value| value.to_str().ok())
            .ok_or_else(ApiErr::unauthorized)?;
        let token = header
            .strip_prefix("Bearer ")
            .ok_or_else(ApiErr::unauthorized)?;
        let claims = state
            .tokens
            .verify_access(token)
            .ok_or_else(ApiErr::unauthorized)?;
        Ok(VoiceAuth {
            user: claims.user_id,
            voice_only: claims.voice_channel,
        })
    }
}

pub(crate) struct ClientIp(pub String);
impl<S: Send + Sync> FromRequestParts<S> for ClientIp {
    type Rejection = std::convert::Infallible;
    async fn from_request_parts(parts: &mut Parts, _: &S) -> Result<Self, Self::Rejection> {
        Ok(ClientIp(
            parts
                .extensions
                .get::<axum::extract::ConnectInfo<SocketAddr>>()
                .map(|info| info.0.ip().to_string())
                .unwrap_or_else(|| "local".into()),
        ))
    }
}

#[derive(Deserialize)]
struct PageQuery {
    before: Option<Uuid>,
    limit: Option<u32>,
}

#[derive(Deserialize)]
struct SignedQuery {
    exp: Option<i64>,
    sig: Option<String>,
}

pub fn router(state: Arc<AppState>) -> Router {
    let origins: Vec<HeaderValue> = state
        .settings
        .server
        .allowed_origins
        .iter()
        .filter_map(|origin| origin.parse().ok())
        .collect();
    let json_api = Router::new()
        .route("/api/v1/auth/register", post(register))
        .route("/api/v1/auth/login", post(login))
        .route("/api/v1/auth/refresh", post(refresh))
        .route("/api/v1/auth/logout", post(logout))
        .route("/api/v1/users/me", get(me).patch(patch_me))
        .route("/api/v1/servers", get(list_servers).post(create_server))
        .route(
            "/api/v1/servers/{id}/channels",
            get(list_channels).post(create_channel),
        )
        .route("/api/v1/dms", get(crate::dm::handler::list).post(crate::dm::handler::open))
        .route("/api/v1/servers/join", post(join_server))
        .route("/api/v1/servers/{id}/moderation", patch(patch_moderation))
        .route(
            "/api/v1/channels/{id}",
            patch(crate::channel::handler::patch).delete(crate::channel::handler::delete),
        )
        .route(
            "/api/v1/channels/{id}/messages",
            get(list_messages).post(send_message),
        )
        .route(
            "/api/v1/channels/{id}/messages/{message_id}",
            patch(edit_message).delete(delete_message),
        )
        .route("/api/v1/channels/{id}/typing", post(start_typing))
        .route("/api/v1/channels/{id}/rtc-token", post(rtc_token))
        .route("/api/v1/channels/{id}/voice/join", post(voice_join))
        .route("/api/v1/channels/{id}/voice-states", get(voice_states))
        .route("/api/v1/voice/leave", post(voice_leave))
        .route("/api/v1/voice/state", patch(voice_state))
        .layer(RequestBodyLimitLayer::new(64 * 1024))
        .layer(TimeoutLayer::with_status_code(
            StatusCode::REQUEST_TIMEOUT,
            Duration::from_secs(10),
        ));
    let upload_limit = (state.settings.storage.max_bytes as usize).saturating_add(1024 * 1024);
    let files = Router::new()
        .route("/api/v1/attachments", post(upload))
        .route("/api/v1/attachments/{id}/content", get(content))
        .route("/api/v1/attachments/{id}/thumbnail", get(thumbnail))
        .layer(DefaultBodyLimit::max(upload_limit.max(2 * 1024 * 1024)))
        .layer(TimeoutLayer::with_status_code(
            StatusCode::REQUEST_TIMEOUT,
            Duration::from_secs(120),
        ));
    Router::new()
        .route("/health/live", get(live))
        .route("/health/ready", get(ready))
        .route("/.well-known/lightchat", get(discovery))
        .route("/gateway", get(gateway::upgrade))
        .merge(crate::web_voice::router())
        .merge(json_api)
        .merge(files)
        .fallback(|| async {
            (
                StatusCode::NOT_FOUND,
                Json(ApiError {
                    code: "not_found".into(),
                    message: "No such endpoint.".into(),
                    retry_after_seconds: None,
                }),
            )
        })
        .with_state(state)
        .layer(
            CorsLayer::new()
                .allow_origin(AllowOrigin::predicate(move |origin, _| {
                    if origins.iter().any(|allowed| allowed == origin) {
                        return true;
                    }
                    origin
                        .to_str()
                        .ok()
                        .and_then(|value| url::Url::parse(value).ok())
                        .is_some_and(|uri| {
                            uri.scheme() == "http"
                                && configuration::is_local_dev_host(uri.host_str())
                        })
                }))
                .allow_methods([
                    Method::GET,
                    Method::POST,
                    Method::PATCH,
                    Method::DELETE,
                    Method::OPTIONS,
                ])
                .allow_headers([
                    header::AUTHORIZATION,
                    header::CONTENT_TYPE,
                    HeaderName::from_static("idempotency-key"),
                ]),
        )
        .layer(TraceLayer::new_for_http())
}

async fn live() -> Json<serde_json::Value> {
    Json(serde_json::json!({"status":"ok","phase":1}))
}

async fn ready(State(state): State<Arc<AppState>>) -> impl IntoResponse {
    match state.store.ping().await {
        Ok(()) => (StatusCode::OK, Json(serde_json::json!({"status":"ok"}))).into_response(),
        Err(_) => (
            StatusCode::SERVICE_UNAVAILABLE,
            Json(ApiError {
                code: "unavailable".into(),
                message: "Database unavailable.".into(),
                retry_after_seconds: None,
            }),
        )
            .into_response(),
    }
}

async fn discovery(
    State(state): State<Arc<AppState>>,
    headers: HeaderMap,
) -> Json<InstanceDiscovery> {
    let origin = configuration::advertised_origin(
        &state.settings.server.public_url,
        headers
            .get(header::HOST)
            .and_then(|value| value.to_str().ok()),
    );
    let gateway = origin
        .replacen("https://", "wss://", 1)
        .replacen("http://", "ws://", 1);
    Json(InstanceDiscovery {
        instance_id: state.settings.server.instance_id,
        name: state.settings.server.name.clone(),
        protocol_version: PROTOCOL_VERSION,
        api_version: API_VERSION,
        api: format!("{origin}/api/v1"),
        gateway: format!("{gateway}/gateway"),
        cdn: state.settings.storage.public_url.clone(),
        rtc: configuration::advertised_rtc_url(
            &state.settings.rtc.public_url,
            headers
                .get(header::HOST)
                .and_then(|value| value.to_str().ok()),
        ),
        max_attachment_bytes: state.settings.storage.max_bytes,
        max_attachments_per_message: MAX_ATTACHMENTS_PER_MESSAGE as u32,
    })
}

async fn register(
    State(state): State<Arc<AppState>>,
    ClientIp(ip): ClientIp,
    Json(body): Json<RegisterRequest>,
) -> ApiResult<Json<AuthResponse>> {
    if !state
        .limiter
        .check(&format!("auth:{ip}"), 10, Duration::from_secs(60))
        .await
    {
        return Err(ApiErr::too_many());
    }
    Ok(Json(
        services::register(&state, body.username, body.display_name, body.password).await?,
    ))
}

async fn login(
    State(state): State<Arc<AppState>>,
    ClientIp(ip): ClientIp,
    Json(body): Json<LoginRequest>,
) -> ApiResult<Json<AuthResponse>> {
    if !state
        .limiter
        .check(&format!("auth:{ip}"), 10, Duration::from_secs(60))
        .await
    {
        return Err(ApiErr::too_many());
    }
    Ok(Json(
        services::login(&state, body.username, body.password).await?,
    ))
}

async fn refresh(
    State(state): State<Arc<AppState>>,
    Json(body): Json<RefreshRequest>,
) -> ApiResult<Json<AuthResponse>> {
    Ok(Json(services::refresh(&state, body.refresh_token).await?))
}

async fn logout(
    State(state): State<Arc<AppState>>,
    Auth(user, session): Auth,
) -> ApiResult<StatusCode> {
    services::logout(&state, user, session).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn me(State(state): State<Arc<AppState>>, Auth(user, _): Auth) -> ApiResult<Json<User>> {
    Ok(Json(services::me(&state, user).await?))
}

async fn patch_me(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Json(body): Json<PatchMeRequest>,
) -> ApiResult<Json<User>> {
    Ok(Json(services::patch_me(&state, user, body).await?))
}

async fn create_server(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Json(body): Json<CreateServerRequest>,
) -> ApiResult<Json<Server>> {
    Ok(Json(
        services::create_server(&state, user, body.name).await?,
    ))
}

async fn list_servers(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
) -> ApiResult<Json<Vec<Server>>> {
    Ok(Json(services::list_servers(&state, user).await?))
}

async fn list_channels(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
) -> ApiResult<Json<Vec<Channel>>> {
    Ok(Json(services::list_channels(&state, user, id).await?))
}

async fn create_channel(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
    Json(body): Json<CreateChannelRequest>,
) -> ApiResult<Json<Channel>> {
    Ok(Json(
        services::create_channel(&state, user, id, body.name, body.kind, body.audio_quality)
            .await?,
    ))
}

async fn join_server(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Json(body): Json<JoinRequest>,
) -> ApiResult<Json<Server>> {
    Ok(Json(
        services::join_server(&state, user, body.invite_code).await?,
    ))
}

async fn patch_moderation(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
    Json(body): Json<PatchModerationRequest>,
) -> ApiResult<Json<Server>> {
    Ok(Json(
        services::patch_moderation(&state, user, id, body.blocked_words, body.cooldown_seconds)
            .await?,
    ))
}

pub(crate) fn advertised_rtc(state: &AppState, headers: &HeaderMap) -> String {
    configuration::advertised_rtc_url(
        &state.settings.rtc.public_url,
        headers
            .get(header::HOST)
            .and_then(|value| value.to_str().ok()),
    )
}

async fn rtc_token(
    State(state): State<Arc<AppState>>,
    auth: VoiceAuth,
    Path(id): Path<Uuid>,
    headers: HeaderMap,
    Json(flags): Json<VoiceFlags>,
) -> ApiResult<Json<chat_protocol::RtcToken>> {
    auth.allow(id)?;
    if !state
        .limiter
        .check(&format!("voice:{}", auth.user), 8, Duration::from_secs(30))
        .await
    {
        return Err(ApiErr::too_many());
    }
    Ok(Json(
        services::join_voice(
            &state,
            auth.user,
            id,
            flags.self_mute,
            flags.self_deaf,
            flags.audio_quality.as_deref(),
            &advertised_rtc(&state, &headers),
            auth.voice_only,
        )
        .await?
        .rtc,
    ))
}

async fn voice_join(
    State(state): State<Arc<AppState>>,
    auth: VoiceAuth,
    Path(id): Path<Uuid>,
    headers: HeaderMap,
    Json(flags): Json<VoiceFlags>,
) -> ApiResult<Json<VoiceJoin>> {
    auth.allow(id)?;
    if !state
        .limiter
        .check(&format!("voice:{}", auth.user), 8, Duration::from_secs(30))
        .await
    {
        return Err(ApiErr::too_many());
    }
    Ok(Json(
        services::join_voice(
            &state,
            auth.user,
            id,
            flags.self_mute,
            flags.self_deaf,
            flags.audio_quality.as_deref(),
            &advertised_rtc(&state, &headers),
            auth.voice_only,
        )
        .await?,
    ))
}

async fn voice_states(
    State(state): State<Arc<AppState>>,
    auth: VoiceAuth,
    Path(id): Path<Uuid>,
) -> ApiResult<Json<Vec<VoiceState>>> {
    auth.allow(id)?;
    Ok(Json(
        services::list_voice(&state, auth.user, id, auth.voice_only).await?,
    ))
}

async fn voice_leave(
    State(state): State<Arc<AppState>>,
    auth: VoiceAuth,
) -> ApiResult<StatusCode> {
    services::leave_voice(&state, auth.user).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn voice_state(
    State(state): State<Arc<AppState>>,
    auth: VoiceAuth,
    Json(flags): Json<VoiceFlags>,
) -> ApiResult<Json<VoiceState>> {
    Ok(Json(
        services::patch_voice(
            &state,
            auth.user,
            flags.self_mute,
            flags.self_deaf,
            flags.audio_quality.as_deref(),
        )
        .await?,
    ))
}

async fn list_messages(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
    Query(query): Query<PageQuery>,
) -> ApiResult<Json<MessagePage>> {
    Ok(Json(
        services::page_messages(&state, user, id, query.before, query.limit).await?,
    ))
}

async fn send_message(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
    headers: HeaderMap,
    Json(body): Json<SendMessageRequest>,
) -> ApiResult<Json<Message>> {
    if !state
        .limiter
        .check(&format!("msg:{user}"), 30, Duration::from_secs(10))
        .await
    {
        return Err(ApiErr::too_many());
    }
    let idempotency = headers
        .get("idempotency-key")
        .and_then(|value| value.to_str().ok())
        .map(str::to_string);
    Ok(Json(
        services::send_message(
            &state,
            user,
            id,
            body.content,
            body.reply_to,
            body.attachment_ids,
            idempotency,
        )
        .await?,
    ))
}

async fn edit_message(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path((id, message_id)): Path<(Uuid, Uuid)>,
    Json(body): Json<PatchMessageRequest>,
) -> ApiResult<Json<Message>> {
    if !state
        .limiter
        .check(&format!("edit:{user}"), 30, Duration::from_secs(10))
        .await
    {
        return Err(ApiErr::too_many());
    }
    Ok(Json(
        services::edit_message(&state, user, id, message_id, body.content).await?,
    ))
}

async fn delete_message(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path((id, message_id)): Path<(Uuid, Uuid)>,
) -> ApiResult<StatusCode> {
    if !state
        .limiter
        .check(&format!("del:{user}"), 30, Duration::from_secs(10))
        .await
    {
        return Err(ApiErr::too_many());
    }
    services::delete_message(&state, user, id, message_id).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn start_typing(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
) -> ApiResult<StatusCode> {
    if !state
        .limiter
        .check(&format!("typing:{user}"), 8, Duration::from_secs(10))
        .await
    {
        return Err(ApiErr::too_many());
    }
    services::start_typing(&state, user, id).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn upload(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    mut multipart: Multipart,
) -> ApiResult<Json<chat_protocol::Attachment>> {
    if !state
        .limiter
        .check(&format!("up:{user}"), 10, Duration::from_secs(60))
        .await
    {
        return Err(ApiErr::too_many());
    }
    while let Some(field) = multipart
        .next_field()
        .await
        .map_err(|_| ApiErr::bad("invalid_upload", "Invalid multipart body."))?
    {
        if field.name() != Some("file") {
            continue;
        }
        let file_name = field.file_name().unwrap_or("file.bin").to_string();
        let mime = field
            .content_type()
            .unwrap_or("application/octet-stream")
            .to_string();
        let tmp = state.objects.tmp_path();
        let size = state
            .objects
            .write_limited(&tmp, field, state.settings.storage.max_bytes)
            .await?;
        return Ok(Json(
            services::upload(&state, user, file_name, mime, tmp, size).await?,
        ));
    }
    Err(ApiErr::bad("invalid_upload", "Missing file field."))
}

async fn content(
    State(state): State<Arc<AppState>>,
    Path(id): Path<Uuid>,
    Query(query): Query<SignedQuery>,
    headers: HeaderMap,
) -> ApiResult<Response> {
    file_response(&state, id, &query, "content", false, &headers).await
}

async fn thumbnail(
    State(state): State<Arc<AppState>>,
    Path(id): Path<Uuid>,
    Query(query): Query<SignedQuery>,
    headers: HeaderMap,
) -> ApiResult<Response> {
    file_response(&state, id, &query, "thumb", true, &headers).await
}

fn bearer_user(state: &AppState, headers: &HeaderMap) -> Option<Uuid> {
    let header = headers.get(header::AUTHORIZATION)?.to_str().ok()?;
    let token = header.strip_prefix("Bearer ")?;
    state
        .tokens
        .verify_access(token)
        .map(|claims| claims.user_id)
}

fn content_disposition(name: &str) -> String {
    let ascii: String = name
        .chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() || matches!(c, '.' | '-' | '_') {
                c
            } else {
                '_'
            }
        })
        .collect();
    let ascii = if ascii.is_empty() {
        "file".into()
    } else {
        ascii
    };
    let mut encoded = String::new();
    for byte in name.as_bytes() {
        if byte.is_ascii_alphanumeric() || matches!(*byte, b'.' | b'-' | b'_') {
            encoded.push(*byte as char);
        } else {
            encoded.push_str(&format!("%{byte:02X}"));
        }
    }
    format!("attachment; filename=\"{ascii}\"; filename*=UTF-8''{encoded}")
}

async fn authorize_file(
    state: &AppState,
    id: Uuid,
    query: &SignedQuery,
    kind: &str,
    headers: &HeaderMap,
) -> ApiResult<chat_domain::message::Attachment> {
    let signed = match (query.exp, query.sig.as_deref()) {
        (Some(exp), Some(sig)) => state.tokens.verify_attachment(id, exp, kind, sig),
        _ => false,
    };
    let (attachment, uploader, message_id) = state
        .store
        .get_attachment(id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    if signed {
        return Ok(attachment);
    }
    let user = bearer_user(state, headers).ok_or_else(ApiErr::unauthorized)?;
    if let Some(message_id) = message_id {
        let channel_id = state
            .store
            .message_channel(message_id)
            .await?
            .ok_or_else(ApiErr::not_found)?;
        services::require_view(state, user, channel_id).await?;
    } else if user != uploader {
        if let Some(owner) = state.store.avatar_owner(id).await?
            && (user == owner || state.store.shares_community(user, owner).await?)
        {
            return Ok(attachment);
        }
        return Err(ApiErr::forbidden());
    }
    Ok(attachment)
}

async fn file_response(
    state: &AppState,
    id: Uuid,
    query: &SignedQuery,
    kind: &str,
    thumb: bool,
    headers: &HeaderMap,
) -> ApiResult<Response> {
    let attachment = authorize_file(state, id, query, kind, headers).await?;
    let path = if thumb {
        state.objects.thumb_path(attachment.object_key)
    } else {
        state.objects.path(attachment.object_key)
    };
    let file = tokio::fs::File::open(&path)
        .await
        .map_err(|_| ApiErr::not_found())?;
    let length = file.metadata().await.map(|meta| meta.len()).unwrap_or(0);
    let mime = if thumb {
        "image/jpeg"
    } else {
        attachment.mime_type.as_str()
    };
    Ok((
        [
            (header::CONTENT_TYPE, mime.to_string()),
            (header::CONTENT_LENGTH, length.to_string()),
            (header::CACHE_CONTROL, "private, max-age=3600".into()),
            (
                header::CONTENT_DISPOSITION,
                if thumb {
                    "inline".into()
                } else {
                    content_disposition(&attachment.file_name)
                },
            ),
        ],
        Body::from_stream(ReaderStream::new(file)),
    )
        .into_response())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{configuration::Settings, objects::ObjectStore, state::AppState};
    use axum::{body::to_bytes, http::Request};
    use tower::ServiceExt;

    async fn test_app() -> Router {
        let dir = std::env::temp_dir().join(format!("chat-test-{}", Uuid::now_v7()));
        std::fs::create_dir_all(&dir).unwrap();
        let mut settings =
            Settings::load(concat!(env!("CARGO_MANIFEST_DIR"), "/../../../config.toml")).unwrap();
        settings.database.url = format!("local:{}", dir.join("chat.json").display());
        settings.storage.local_dir = dir.join("objects").display().to_string();
        let store = crate::database::open(&settings.database).await.unwrap();
        let objects = ObjectStore::open(&settings.storage.local_dir).unwrap();
        router(AppState::new(settings, store, objects))
    }

    async fn json<T: serde::de::DeserializeOwned>(response: axum::response::Response) -> T {
        let body = to_bytes(response.into_body(), 1024 * 1024).await.unwrap();
        serde_json::from_slice(&body).unwrap()
    }

    async fn get_json(
        app: &Router,
        uri: &str,
        token: Option<&str>,
    ) -> axum::response::Response {
        let mut builder = Request::builder().method("GET").uri(uri);
        if let Some(token) = token {
            builder = builder.header("authorization", format!("Bearer {token}"));
        }
        app.clone()
            .oneshot(builder.body(Body::empty()).unwrap())
            .await
            .unwrap()
    }

    async fn post_json(
        app: &Router,
        uri: &str,
        token: Option<&str>,
        body: &str,
    ) -> axum::response::Response {
        let mut builder = Request::builder()
            .method("POST")
            .uri(uri)
            .header("content-type", "application/json");
        if let Some(token) = token {
            builder = builder.header("authorization", format!("Bearer {token}"));
        }
        app.clone()
            .oneshot(builder.body(Body::from(body.to_string())).unwrap())
            .await
            .unwrap()
    }

    #[tokio::test]
    async fn discovery_and_auth_and_messages() {
        let app = test_app().await;
        let response = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri("/.well-known/lightchat")
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::OK);
        let info: InstanceDiscovery = json(response).await;
        assert_eq!(info.protocol_version, 1);

        let lan = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri("/.well-known/lightchat")
                    .header("host", "192.168.1.10:8080")
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        let lan: InstanceDiscovery = json(lan).await;
        assert_eq!(lan.api, "http://192.168.1.10:8080/api/v1");
        assert_eq!(lan.gateway, "ws://192.168.1.10:8080/gateway");
        assert_eq!(lan.rtc, "http://192.168.1.10:7880");
        assert_eq!(lan.rtc, "http://192.168.1.10:7880");

        let denied = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri("/api/v1/servers")
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(denied.status(), StatusCode::UNAUTHORIZED);

        let register = post_json(
            &app,
            "/api/v1/auth/register",
            None,
            r#"{"username":"alice","display_name":"Alice","password":"password1"}"#,
        )
        .await;
        assert_eq!(register.status(), StatusCode::OK);
        let alice: AuthResponse = json(register).await;

        let login = post_json(
            &app,
            "/api/v1/auth/login",
            None,
            r#"{"username":"alice","password":"password1"}"#,
        )
        .await;
        assert_eq!(login.status(), StatusCode::OK);

        let created = post_json(
            &app,
            "/api/v1/servers",
            Some(&alice.access_token),
            r#"{"name":"Friends"}"#,
        )
        .await;
        assert_eq!(created.status(), StatusCode::OK);
        let server: Server = json(created).await;

        let channels = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri(format!("/api/v1/servers/{}/channels", server.id))
                    .header("authorization", format!("Bearer {}", alice.access_token))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        let channels: Vec<Channel> = json(channels).await;
        assert!(channels.iter().any(|c| c.kind == "voice"));
        let channel = channels.iter().find(|c| c.kind == "text").unwrap();
        let voice = channels.iter().find(|c| c.kind == "voice").unwrap();

        let mut send = Request::builder()
            .method("POST")
            .uri(format!("/api/v1/channels/{}/messages", channel.id))
            .header("authorization", format!("Bearer {}", alice.access_token))
            .header("content-type", "application/json")
            .header("idempotency-key", "idem-key-1")
            .body(Body::from(r#"{"content":"hello"}"#))
            .unwrap();
        let sent = app.clone().oneshot(send).await.unwrap();
        assert_eq!(sent.status(), StatusCode::OK);
        let message: Message = json(sent).await;
        assert_eq!(message.content.as_deref(), Some("hello"));

        send = Request::builder()
            .method("POST")
            .uri(format!("/api/v1/channels/{}/messages", channel.id))
            .header("authorization", format!("Bearer {}", alice.access_token))
            .header("content-type", "application/json")
            .header("idempotency-key", "idem-key-1")
            .body(Body::from(r#"{"content":"hello"}"#))
            .unwrap();
        let again = app.clone().oneshot(send).await.unwrap();
        let same: Message = json(again).await;
        assert_eq!(same.id, message.id);

        let bob = json::<AuthResponse>(
            post_json(
                &app,
                "/api/v1/auth/register",
                None,
                r#"{"username":"bob","display_name":"Bob","password":"password1"}"#,
            )
            .await,
        )
        .await;
        let forbidden = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&bob.access_token),
            r#"{"content":"nope"}"#,
        )
        .await;
        assert_eq!(forbidden.status(), StatusCode::FORBIDDEN);

        let joined = post_json(
            &app,
            "/api/v1/servers/join",
            Some(&bob.access_token),
            &format!(r#"{{"invite_code":"{}"}}"#, server.invite_code),
        )
        .await;
        assert_eq!(joined.status(), StatusCode::OK);
        let ok = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&bob.access_token),
            r#"{"content":"hi alice"}"#,
        )
        .await;
        assert_eq!(ok.status(), StatusCode::OK);

        let mention = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&alice.access_token),
            r#"{"content":"hey @bob"}"#,
        )
        .await;
        assert_eq!(mention.status(), StatusCode::OK);
        let mentioned: Message = json(mention).await;
        assert_eq!(mentioned.mentions, vec![bob.user.id]);
        assert!(!mentioned.mention_everyone);

        let everyone = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&alice.access_token),
            r#"{"content":"ping @everyone and @bob"}"#,
        )
        .await;
        assert_eq!(everyone.status(), StatusCode::OK);
        let everyone: Message = json(everyone).await;
        assert!(everyone.mention_everyone);
        assert!(!everyone.mention_here);
        assert_eq!(everyone.mentions, vec![bob.user.id]);

        let here = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&alice.access_token),
            r#"{"content":"ping @here"}"#,
        )
        .await;
        assert_eq!(here.status(), StatusCode::OK);
        let here: Message = json(here).await;
        assert!(here.mention_here);
        assert!(!here.mention_everyone);
        assert!(here.mentions.is_empty());

        let edited = patch_json(
            &app,
            &format!("/api/v1/channels/{}/messages/{}", channel.id, mentioned.id),
            &alice.access_token,
            r#"{"content":"hey @bob again"}"#,
        )
        .await;
        assert_eq!(edited.status(), StatusCode::OK);
        let edited: Message = json(edited).await;
        assert_eq!(edited.content.as_deref(), Some("hey @bob again"));
        assert!(edited.edited_at.is_some());
        assert_eq!(edited.mentions, vec![bob.user.id]);

        let denied_edit = patch_json(
            &app,
            &format!("/api/v1/channels/{}/messages/{}", channel.id, mentioned.id),
            &bob.access_token,
            r#"{"content":"nope"}"#,
        )
        .await;
        assert_eq!(denied_edit.status(), StatusCode::FORBIDDEN);

        let reply = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&bob.access_token),
            &format!(r#"{{"content":"re","reply_to":"{}"}}"#, mentioned.id),
        )
        .await;
        assert_eq!(reply.status(), StatusCode::OK);
        let replied: Message = json(reply).await;
        assert_eq!(replied.reply_to, Some(mentioned.id));
        assert_eq!(replied.mentions, vec![alice.user.id]);

        let bad_reply = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&alice.access_token),
            r#"{"content":"no","reply_to":"01950000-0000-7000-8000-000000000099"}"#,
        )
        .await;
        assert_eq!(bad_reply.status(), StatusCode::BAD_REQUEST);

        let typing = post_json(
            &app,
            &format!("/api/v1/channels/{}/typing", channel.id),
            Some(&bob.access_token),
            "{}",
        )
        .await;
        assert_eq!(typing.status(), StatusCode::NO_CONTENT);

        let denied_delete = delete_json(
            &app,
            &format!("/api/v1/channels/{}/messages/{}", channel.id, mentioned.id),
            &bob.access_token,
        )
        .await;
        assert_eq!(denied_delete.status(), StatusCode::FORBIDDEN);

        let deleted = delete_json(
            &app,
            &format!("/api/v1/channels/{}/messages/{}", channel.id, mentioned.id),
            &alice.access_token,
        )
        .await;
        assert_eq!(deleted.status(), StatusCode::NO_CONTENT);

        let gone = delete_json(
            &app,
            &format!("/api/v1/channels/{}/messages/{}", channel.id, mentioned.id),
            &alice.access_token,
        )
        .await;
        assert_eq!(gone.status(), StatusCode::NOT_FOUND);

        let joined_voice = post_json(
            &app,
            &format!("/api/v1/channels/{}/voice/join", voice.id),
            Some(&alice.access_token),
            r#"{"self_mute":false,"self_deaf":false}"#,
        )
        .await;
        assert_eq!(joined_voice.status(), StatusCode::OK);
        let session: VoiceJoin = json(joined_voice).await;
        assert_eq!(session.rtc.token.split('.').count(), 3);
        assert_eq!(session.rtc.url, "ws://localhost:7880");
        assert_eq!(session.state.channel_id, Some(voice.id));
        let lan_voice = app
            .clone()
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri(format!("/api/v1/channels/{}/voice/join", voice.id))
                    .header("authorization", format!("Bearer {}", alice.access_token))
                    .header("content-type", "application/json")
                    .header("host", "10.19.144.83:8080")
                    .body(Body::from(r#"{"self_mute":false,"self_deaf":false}"#))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(lan_voice.status(), StatusCode::OK);
        let lan_voice: VoiceJoin = json(lan_voice).await;
        assert_eq!(lan_voice.rtc.url, "ws://10.19.144.83:7880");
        assert_eq!(session.audio.id, "studio");
        assert_eq!(session.audio.bitrate_bps, 510_000);
        assert_eq!(session.audio.channels, 2);
        assert_eq!(session.max_audio_quality, "studio");
        assert_eq!(voice.audio_quality.as_deref(), Some("studio"));
        let muted = patch_json(
            &app,
            "/api/v1/voice/state",
            &alice.access_token,
            r#"{"self_mute":true}"#,
        )
        .await;
        assert_eq!(muted.status(), StatusCode::OK);
        let muted: VoiceState = json(muted).await;
        assert!(muted.self_mute);
        assert_eq!(muted.audio_quality, "studio");
        let high = post_json(
            &app,
            &format!("/api/v1/channels/{}/voice/join", voice.id),
            Some(&alice.access_token),
            r#"{"audio_quality":"high"}"#,
        )
        .await;
        assert_eq!(high.status(), StatusCode::OK);
        let high: VoiceJoin = json(high).await;
        assert_eq!(high.audio.id, "high");
        assert_eq!(high.audio.bitrate_bps, 128_000);
        let very = post_json(
            &app,
            &format!("/api/v1/channels/{}/voice/join", voice.id),
            Some(&alice.access_token),
            r#"{"audio_quality":"very_high"}"#,
        )
        .await;
        assert_eq!(very.status(), StatusCode::OK);
        let very: VoiceJoin = json(very).await;
        assert_eq!(very.audio.id, "very_high");
        assert_eq!(very.audio.bitrate_bps, 384_000);
        let cap = patch_json(
            &app,
            &format!("/api/v1/channels/{}", voice.id),
            &alice.access_token,
            r#"{"audio_quality":"high"}"#,
        )
        .await;
        assert_eq!(cap.status(), StatusCode::OK);
        let capped: Channel = json(cap).await;
        assert_eq!(capped.audio_quality.as_deref(), Some("high"));
        let clamped = post_json(
            &app,
            &format!("/api/v1/channels/{}/voice/join", voice.id),
            Some(&alice.access_token),
            r#"{"audio_quality":"studio"}"#,
        )
        .await;
        assert_eq!(clamped.status(), StatusCode::OK);
        let clamped: VoiceJoin = json(clamped).await;
        assert_eq!(clamped.audio.id, "high");
        assert_eq!(clamped.max_audio_quality, "high");
        let invalid = post_json(
            &app,
            &format!("/api/v1/channels/{}/voice/join", voice.id),
            Some(&alice.access_token),
            r#"{"audio_quality":"lossless"}"#,
        )
        .await;
        assert_eq!(invalid.status(), StatusCode::BAD_REQUEST);
        let restore = patch_json(
            &app,
            &format!("/api/v1/channels/{}", voice.id),
            &alice.access_token,
            r#"{"audio_quality":"studio"}"#,
        )
        .await;
        assert_eq!(restore.status(), StatusCode::OK);
        let eve: AuthResponse = json(
            post_json(
                &app,
                "/api/v1/auth/register",
                None,
                r#"{"username":"eve","display_name":"Eve","password":"password1"}"#,
            )
            .await,
        )
        .await;
        let forbidden_voice = post_json(
            &app,
            &format!("/api/v1/channels/{}/voice/join", voice.id),
            Some(&eve.access_token),
            r#"{}"#,
        )
        .await;
        assert_eq!(forbidden_voice.status(), StatusCode::FORBIDDEN);
        let bob_ok = post_json(
            &app,
            &format!("/api/v1/channels/{}/voice/join", voice.id),
            Some(&bob.access_token),
            r#"{}"#,
        )
        .await;
        assert_eq!(bob_ok.status(), StatusCode::OK);
    }

    #[tokio::test]
    async fn web_voice_guest_cannot_read_other_channels() {
        let app = test_app().await;
        let alice: AuthResponse = json(
            post_json(
                &app,
                "/api/v1/auth/register",
                None,
                r#"{"username":"alice","display_name":"Alice","password":"password1"}"#,
            )
            .await,
        )
        .await;
        let created = post_json(
            &app,
            "/api/v1/servers",
            Some(&alice.access_token),
            r#"{"name":"Friends"}"#,
        )
        .await;
        let server: Server = json(created).await;
        let channels: Vec<Channel> = json(
            get_json(
                &app,
                &format!("/api/v1/servers/{}/channels", server.id),
                Some(&alice.access_token),
            )
            .await,
        )
        .await;
        let text = channels.iter().find(|c| c.kind == "text").unwrap();
        let voice = channels.iter().find(|c| c.kind == "voice").unwrap();

        let page = get_json(&app, &format!("/voice/{}", voice.id), None).await;
        assert_eq!(page.status(), StatusCode::OK);
        let html = String::from_utf8(
            to_bytes(page.into_body(), 64 * 1024).await.unwrap().to_vec(),
        )
        .unwrap();
        assert!(html.contains("voice/app.js"));
        let hidden_page = get_json(&app, &format!("/voice/{}", text.id), None).await;
        assert_eq!(hidden_page.status(), StatusCode::NOT_FOUND);

        let hidden = get_json(&app, &format!("/api/v1/voice/rooms/{}", text.id), None).await;
        assert_eq!(hidden.status(), StatusCode::NOT_FOUND);
        let preview = get_json(&app, &format!("/api/v1/voice/rooms/{}", voice.id), None).await;
        assert_eq!(preview.status(), StatusCode::OK);
        let room: chat_protocol::VoiceRoom = json(preview).await;
        assert_eq!(room.kind, "voice");
        assert_eq!(room.name, voice.name);

        let guest_res = post_json(
            &app,
            &format!("/api/v1/voice/rooms/{}/guest", voice.id),
            None,
            r#"{"display_name":"Ada"}"#,
        )
        .await;
        assert_eq!(guest_res.status(), StatusCode::OK);
        let guest: chat_protocol::VoiceGuestSession = json(guest_res).await;
        assert_eq!(guest.room.channel_id, voice.id);
        assert_eq!(guest.join.state.display_name, "Ada");

        let servers = get_json(&app, "/api/v1/servers", Some(&guest.access_token)).await;
        assert_eq!(servers.status(), StatusCode::FORBIDDEN);
        let listed = get_json(
            &app,
            &format!("/api/v1/servers/{}/channels", server.id),
            Some(&guest.access_token),
        )
        .await;
        assert_eq!(listed.status(), StatusCode::FORBIDDEN);
        let messages = get_json(
            &app,
            &format!("/api/v1/channels/{}/messages", text.id),
            Some(&guest.access_token),
        )
        .await;
        assert_eq!(messages.status(), StatusCode::FORBIDDEN);
        let other_voice = post_json(
            &app,
            &format!("/api/v1/channels/{}/voice/join", text.id),
            Some(&guest.access_token),
            r#"{}"#,
        )
        .await;
        assert_eq!(other_voice.status(), StatusCode::FORBIDDEN);
        let states = get_json(
            &app,
            &format!("/api/v1/channels/{}/voice-states", voice.id),
            Some(&guest.access_token),
        )
        .await;
        assert_eq!(states.status(), StatusCode::OK);
        let states: Vec<VoiceState> = json(states).await;
        assert!(states.iter().any(|row| row.display_name == "Ada"));
    }

    const PNG_1X1: &[u8] = &[
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44,
        0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90,
        0x77, 0x53, 0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8,
        0xCF, 0xC0, 0x00, 0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00,
        0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    fn multipart(name: &str, mime: &str, bytes: &[u8]) -> (String, Vec<u8>) {
        let boundary = "testboundary";
        let mut body = Vec::new();
        body.extend_from_slice(
            format!("--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\nContent-Type: {mime}\r\n\r\n")
                .as_bytes(),
        );
        body.extend_from_slice(bytes);
        body.extend_from_slice(format!("\r\n--{boundary}--\r\n").as_bytes());
        (format!("multipart/form-data; boundary={boundary}"), body)
    }

    async fn post_file(
        app: &Router,
        token: &str,
        name: &str,
        mime: &str,
        bytes: &[u8],
    ) -> axum::response::Response {
        let (content_type, body) = multipart(name, mime, bytes);
        app.clone()
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri("/api/v1/attachments")
                    .header("authorization", format!("Bearer {token}"))
                    .header("content-type", content_type)
                    .body(Body::from(body))
                    .unwrap(),
            )
            .await
            .unwrap()
    }

    #[tokio::test]
    async fn upload_image_and_file_then_download() {
        let app = test_app().await;
        let alice: AuthResponse = json(
            post_json(
                &app,
                "/api/v1/auth/register",
                None,
                r#"{"username":"alice","display_name":"Alice","password":"password1"}"#,
            )
            .await,
        )
        .await;
        let created: Server = json(
            post_json(
                &app,
                "/api/v1/servers",
                Some(&alice.access_token),
                r#"{"name":"Friends"}"#,
            )
            .await,
        )
        .await;
        let channels: Vec<Channel> = json(
            app.clone()
                .oneshot(
                    Request::builder()
                        .uri(format!("/api/v1/servers/{}/channels", created.id))
                        .header("authorization", format!("Bearer {}", alice.access_token))
                        .body(Body::empty())
                        .unwrap(),
                )
                .await
                .unwrap(),
        )
        .await;
        let channel = channels.iter().find(|c| c.kind == "text").unwrap();

        let image = post_file(&app, &alice.access_token, "pic.png", "image/png", PNG_1X1).await;
        assert_eq!(image.status(), StatusCode::OK);
        let image: chat_protocol::Attachment = json(image).await;
        assert_eq!(image.mime_type, "image/png");

        let notes = post_file(
            &app,
            &alice.access_token,
            "notes.txt",
            "text/plain",
            b"hello file",
        )
        .await;
        assert_eq!(notes.status(), StatusCode::OK);
        let notes: chat_protocol::Attachment = json(notes).await;
        assert_eq!(notes.mime_type, "text/plain");
        assert!(notes.thumbnail_url.is_none());

        let sent = app
            .clone()
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri(format!("/api/v1/channels/{}/messages", channel.id))
                    .header("authorization", format!("Bearer {}", alice.access_token))
                    .header("content-type", "application/json")
                    .header("idempotency-key", "file-msg-1")
                    .body(Body::from(format!(
                        r#"{{"content":null,"attachment_ids":["{}","{}"]}}"#,
                        image.id, notes.id
                    )))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(sent.status(), StatusCode::OK);
        let message: Message = json(sent).await;
        assert_eq!(message.attachments.len(), 2);

        let remote = url::Url::parse(&message.attachments[1].download_url).unwrap();
        let signed = format!("{}?{}", remote.path(), remote.query().unwrap_or_default());
        let download = app
            .clone()
            .oneshot(Request::builder().uri(signed).body(Body::empty()).unwrap())
            .await
            .unwrap();
        assert_eq!(download.status(), StatusCode::OK);
        let body = to_bytes(download.into_body(), 1024).await.unwrap();
        assert_eq!(&body[..], b"hello file");

        let auth_download = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri(format!("/api/v1/attachments/{}/content", notes.id))
                    .header("authorization", format!("Bearer {}", alice.access_token))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(auth_download.status(), StatusCode::OK);

        let bob: AuthResponse = json(
            post_json(
                &app,
                "/api/v1/auth/register",
                None,
                r#"{"username":"bob","display_name":"Bob","password":"password1"}"#,
            )
            .await,
        )
        .await;
        let joined = post_json(
            &app,
            "/api/v1/servers/join",
            Some(&bob.access_token),
            &format!(r#"{{"invite_code":"{}"}}"#, created.invite_code),
        )
        .await;
        assert_eq!(joined.status(), StatusCode::OK);
        let listed = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri(format!("/api/v1/channels/{}/messages", channel.id))
                    .header("authorization", format!("Bearer {}", bob.access_token))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        let page: chat_protocol::MessagePage = json(listed).await;
        let file = page
            .items
            .iter()
            .flat_map(|item| item.attachments.iter())
            .find(|item| item.file_name == "notes.txt")
            .expect("bob should see the text file");
        let peer = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri(format!("/api/v1/attachments/{}/content", file.id))
                    .header("authorization", format!("Bearer {}", bob.access_token))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(peer.status(), StatusCode::OK);
        let peer_body = to_bytes(peer.into_body(), 1024).await.unwrap();
        assert_eq!(&peer_body[..], b"hello file");
    }

    #[tokio::test]
    async fn blocked_words_and_cooldown() {
        let app = test_app().await;
        let alice: AuthResponse = json(
            post_json(
                &app,
                "/api/v1/auth/register",
                None,
                r#"{"username":"mod","display_name":"Mod","password":"password1"}"#,
            )
            .await,
        )
        .await;
        let server: Server = json(
            post_json(
                &app,
                "/api/v1/servers",
                Some(&alice.access_token),
                r#"{"name":"Moderated"}"#,
            )
            .await,
        )
        .await;
        let updated = patch_json(
            &app,
            &format!("/api/v1/servers/{}/moderation", server.id),
            &alice.access_token,
            r#"{"blocked_words":["banana"],"cooldown_seconds":60}"#,
        )
        .await;
        assert_eq!(updated.status(), StatusCode::OK);
        let updated: Server = json(updated).await;
        assert_eq!(updated.cooldown_seconds, 60);
        assert!(updated.blocked_words.iter().any(|w| w == "banana"));
        let channels: Vec<Channel> = json(
            app.clone()
                .oneshot(
                    Request::builder()
                        .uri(format!("/api/v1/servers/{}/channels", server.id))
                        .header("authorization", format!("Bearer {}", alice.access_token))
                        .body(Body::empty())
                        .unwrap(),
                )
                .await
                .unwrap(),
        )
        .await;
        let channel = channels.iter().find(|c| c.kind == "text").unwrap();
        let blocked = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&alice.access_token),
            r#"{"content":"I like Banana pie"}"#,
        )
        .await;
        assert_eq!(blocked.status(), StatusCode::BAD_REQUEST);
        let blocked_body: serde_json::Value = json(blocked).await;
        assert_eq!(blocked_body["code"], "blocked_word");
        let ok = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&alice.access_token),
            r#"{"content":"hello there"}"#,
        )
        .await;
        assert_eq!(ok.status(), StatusCode::OK);
        let cooled = post_json(
            &app,
            &format!("/api/v1/channels/{}/messages", channel.id),
            Some(&alice.access_token),
            r#"{"content":"another"}"#,
        )
        .await;
        assert_eq!(cooled.status(), StatusCode::TOO_MANY_REQUESTS);
        let cooled_body: serde_json::Value = json(cooled).await;
        assert_eq!(cooled_body["code"], "cooldown");
        assert!(cooled_body["retry_after_seconds"].as_u64().unwrap() >= 1);
    }

    async fn delete_json(
        app: &Router,
        uri: &str,
        token: &str,
    ) -> axum::response::Response {
        app.clone()
            .oneshot(
                Request::builder()
                    .method("DELETE")
                    .uri(uri)
                    .header("authorization", format!("Bearer {token}"))
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap()
    }

    async fn patch_json(
        app: &Router,
        uri: &str,
        token: &str,
        body: &str,
    ) -> axum::response::Response {
        app.clone()
            .oneshot(
                Request::builder()
                    .method("PATCH")
                    .uri(uri)
                    .header("authorization", format!("Bearer {token}"))
                    .header("content-type", "application/json")
                    .body(Body::from(body.to_string()))
                    .unwrap(),
            )
            .await
            .unwrap()
    }

    fn animated_gif() -> Vec<u8> {
        use image::{Delay, Frame, Rgba, RgbaImage, codecs::gif::GifEncoder};
        let mut buf = Vec::new();
        {
            let mut encoder = GifEncoder::new(&mut buf);
            encoder
                .set_repeat(image::codecs::gif::Repeat::Infinite)
                .unwrap();
            let red = RgbaImage::from_pixel(2, 2, Rgba([255, 0, 0, 255]));
            let green = RgbaImage::from_pixel(2, 2, Rgba([0, 255, 0, 255]));
            encoder
                .encode_frame(Frame::from_parts(
                    red,
                    0,
                    0,
                    Delay::from_numer_denom_ms(80, 1),
                ))
                .unwrap();
            encoder
                .encode_frame(Frame::from_parts(
                    green,
                    0,
                    0,
                    Delay::from_numer_denom_ms(80, 1),
                ))
                .unwrap();
        }
        buf
    }

    #[tokio::test]
    async fn patch_username_and_animated_avatar() {
        let app = test_app().await;
        let alice: AuthResponse = json(
            post_json(
                &app,
                "/api/v1/auth/register",
                None,
                r#"{"username":"alice","display_name":"Alice","password":"password1"}"#,
            )
            .await,
        )
        .await;
        let bob: AuthResponse = json(
            post_json(
                &app,
                "/api/v1/auth/register",
                None,
                r#"{"username":"bob","display_name":"Bob","password":"password1"}"#,
            )
            .await,
        )
        .await;

        let renamed = patch_json(
            &app,
            "/api/v1/users/me",
            &alice.access_token,
            r#"{"username":"alice2","display_name":"Ada"}"#,
        )
        .await;
        assert_eq!(renamed.status(), StatusCode::OK);
        let me: User = json(renamed).await;
        assert_eq!(me.username, "alice2");
        assert_eq!(me.display_name, "Ada");
        assert!(me.avatar.is_none());

        let login = post_json(
            &app,
            "/api/v1/auth/login",
            None,
            r#"{"username":"alice2","password":"password1"}"#,
        )
        .await;
        assert_eq!(login.status(), StatusCode::OK);

        let taken = patch_json(
            &app,
            "/api/v1/users/me",
            &alice.access_token,
            r#"{"username":"bob"}"#,
        )
        .await;
        assert_eq!(taken.status(), StatusCode::CONFLICT);

        let gif = post_file(
            &app,
            &alice.access_token,
            "spin.gif",
            "image/gif",
            &animated_gif(),
        )
        .await;
        assert_eq!(gif.status(), StatusCode::OK);
        let gif: chat_protocol::Attachment = json(gif).await;
        let set = patch_json(
            &app,
            "/api/v1/users/me",
            &alice.access_token,
            &format!(r#"{{"avatar_id":"{}"}}"#, gif.id),
        )
        .await;
        assert_eq!(set.status(), StatusCode::OK);
        let me: User = json(set).await;
        let avatar = me.avatar.expect("avatar");
        assert!(avatar.animated);
        assert_eq!(avatar.mime_type, "image/gif");
        assert!(avatar.thumbnail_url.is_some());
        assert!(me.banner.is_none());

        let banner_gif = post_file(
            &app,
            &alice.access_token,
            "cover.gif",
            "image/gif",
            &animated_gif(),
        )
        .await;
        assert_eq!(banner_gif.status(), StatusCode::OK);
        let banner_gif: chat_protocol::Attachment = json(banner_gif).await;
        let set_banner = patch_json(
            &app,
            "/api/v1/users/me",
            &alice.access_token,
            &format!(r#"{{"banner_id":"{}"}}"#, banner_gif.id),
        )
        .await;
        assert_eq!(set_banner.status(), StatusCode::OK);
        let me: User = json(set_banner).await;
        let banner = me.banner.expect("banner");
        assert!(banner.animated);
        assert_eq!(banner.mime_type, "image/gif");
        assert!(me.avatar.is_some());

        let notes = post_file(
            &app,
            &alice.access_token,
            "notes.txt",
            "text/plain",
            b"not an image",
        )
        .await;
        let notes: chat_protocol::Attachment = json(notes).await;
        let bad = patch_json(
            &app,
            "/api/v1/users/me",
            &alice.access_token,
            &format!(r#"{{"avatar_id":"{}"}}"#, notes.id),
        )
        .await;
        assert_eq!(bad.status(), StatusCode::BAD_REQUEST);

        let _ = post_json(
            &app,
            "/api/v1/servers",
            Some(&alice.access_token),
            r#"{"name":"Friends"}"#,
        )
        .await;
        let steal = patch_json(
            &app,
            "/api/v1/users/me",
            &bob.access_token,
            &format!(r#"{{"avatar_id":"{}"}}"#, gif.id),
        )
        .await;
        assert_eq!(steal.status(), StatusCode::BAD_REQUEST);
    }
}
