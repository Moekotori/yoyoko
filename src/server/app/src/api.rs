use crate::{
    configuration,
    error::{ApiErr, ApiResult},
    gateway,
    identity::AccessClaims,
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
    InstanceDiscovery, JoinRequest, LoginRequest, Message, MessagePage, PROTOCOL_VERSION,
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
        let AccessClaims {
            user_id,
            session_id,
        } = state
            .tokens
            .verify_access(token)
            .map_err(|_| ApiErr::unauthorized())?;
        Ok(Auth(user_id, session_id))
    }
}

struct ClientIp(String);
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
    exp: i64,
    sig: String,
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
        .route("/api/v1/users/me", get(me))
        .route("/api/v1/servers", get(list_servers).post(create_server))
        .route(
            "/api/v1/servers/{id}/channels",
            get(list_channels).post(create_channel),
        )
        .route("/api/v1/servers/join", post(join_server))
        .route(
            "/api/v1/channels/{id}/messages",
            get(list_messages).post(send_message),
        )
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
    let files = Router::new()
        .route("/api/v1/attachments", post(upload))
        .route("/api/v1/attachments/{id}/content", get(content))
        .route("/api/v1/attachments/{id}/thumbnail", get(thumbnail))
        .layer(DefaultBodyLimit::max(26 * 1024 * 1024))
        .layer(TimeoutLayer::with_status_code(
            StatusCode::REQUEST_TIMEOUT,
            Duration::from_secs(120),
        ));
    Router::new()
        .route("/health/live", get(live))
        .route("/health/ready", get(ready))
        .route("/.well-known/lightchat", get(discovery))
        .route("/gateway", get(gateway::upgrade))
        .merge(json_api)
        .merge(files)
        .fallback(|| async {
            (
                StatusCode::NOT_FOUND,
                Json(ApiError {
                    code: "not_found".into(),
                    message: "No such endpoint.".into(),
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
        rtc: state.settings.rtc.public_url.clone(),
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
    let user = state
        .store
        .find_user(user)
        .await?
        .ok_or_else(ApiErr::unauthorized)?;
    Ok(Json(User {
        id: user.id,
        username: user.username,
        display_name: user.display_name,
    }))
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
        services::create_channel(&state, user, id, body.name, body.kind).await?,
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

async fn rtc_token(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
    Json(flags): Json<VoiceFlags>,
) -> ApiResult<Json<chat_protocol::RtcToken>> {
    if !state
        .limiter
        .check(&format!("voice:{user}"), 8, Duration::from_secs(30))
        .await
    {
        return Err(ApiErr::too_many());
    }
    Ok(Json(
        services::join_voice(&state, user, id, flags.self_mute, flags.self_deaf)
            .await?
            .rtc,
    ))
}

async fn voice_join(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
    Json(flags): Json<VoiceFlags>,
) -> ApiResult<Json<VoiceJoin>> {
    if !state
        .limiter
        .check(&format!("voice:{user}"), 8, Duration::from_secs(30))
        .await
    {
        return Err(ApiErr::too_many());
    }
    Ok(Json(
        services::join_voice(&state, user, id, flags.self_mute, flags.self_deaf).await?,
    ))
}

async fn voice_states(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
) -> ApiResult<Json<Vec<VoiceState>>> {
    Ok(Json(services::list_voice(&state, user, id).await?))
}

async fn voice_leave(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
) -> ApiResult<StatusCode> {
    services::leave_voice(&state, user).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn voice_state(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Json(flags): Json<VoiceFlags>,
) -> ApiResult<Json<VoiceState>> {
    Ok(Json(
        services::patch_voice(&state, user, flags.self_mute, flags.self_deaf).await?,
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
        let file_name = field.file_name().unwrap_or("image.png").to_string();
        let mime = field
            .content_type()
            .unwrap_or("application/octet-stream")
            .to_string();
        let tmp = state.objects.tmp_path();
        let size = state.objects.write_limited(&tmp, field).await?;
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
) -> ApiResult<Response> {
    file_response(&state, id, &query, "content", false).await
}

async fn thumbnail(
    State(state): State<Arc<AppState>>,
    Path(id): Path<Uuid>,
    Query(query): Query<SignedQuery>,
) -> ApiResult<Response> {
    file_response(&state, id, &query, "thumb", true).await
}

async fn file_response(
    state: &AppState,
    id: Uuid,
    query: &SignedQuery,
    kind: &str,
    thumb: bool,
) -> ApiResult<Response> {
    if !state
        .tokens
        .verify_attachment(id, query.exp, kind, &query.sig)
    {
        return Err(ApiErr::unauthorized());
    }
    let (attachment, _, _) = state
        .store
        .get_attachment(id)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    let path = if thumb {
        state.objects.thumb_path(attachment.object_key)
    } else {
        state.objects.path(attachment.object_key)
    };
    let file = tokio::fs::File::open(&path)
        .await
        .map_err(|_| ApiErr::not_found())?;
    let mime = if thumb {
        "image/jpeg"
    } else {
        attachment.mime_type.as_str()
    };
    Ok((
        [
            (header::CONTENT_TYPE, mime.to_string()),
            (header::CACHE_CONTROL, "private, max-age=3600".into()),
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
        assert_eq!(session.state.channel_id, Some(voice.id));
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
}
