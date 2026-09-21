mod service;

use crate::{
    api::{ClientIp, advertised_rtc},
    error::{ApiErr, ApiResult},
    state::AppState,
};
use axum::{
    Json, Router,
    extract::{Path, State},
    http::{HeaderMap, StatusCode, header},
    response::{Html, IntoResponse, Response},
    routing::{get, post},
};
use std::{sync::Arc, time::Duration};
use uuid::Uuid;

pub fn router() -> Router<Arc<AppState>> {
    Router::new()
        .route("/voice/app.css", get(css))
        .route("/voice/app.js", get(js))
        .route("/voice/livekit-client.js", get(livekit))
        .route("/voice/{id}", get(page))
        .route("/api/v1/voice/rooms/{id}", get(preview))
        .route("/api/v1/voice/rooms/{id}/guest", post(guest))
}

async fn page(State(state): State<Arc<AppState>>, Path(id): Path<String>) -> Response {
    let Ok(channel_id) = id.parse::<Uuid>() else {
        return not_found_page();
    };
    if service::preview(&state, channel_id).await.is_err() {
        return not_found_page();
    }
    (
        [(header::CONTENT_TYPE, "text/html; charset=utf-8")],
        include_str!("../../web/voice.html"),
    )
        .into_response()
}

fn not_found_page() -> Response {
    (
        StatusCode::NOT_FOUND,
        Html("<!doctype html><title>Not found</title>"),
    )
        .into_response()
}

async fn css() -> Response {
    (
        [(header::CONTENT_TYPE, "text/css; charset=utf-8")],
        include_str!("../../web/voice.css"),
    )
        .into_response()
}

async fn js() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            "application/javascript; charset=utf-8",
        )],
        include_str!("../../web/voice.js"),
    )
        .into_response()
}

async fn livekit() -> Response {
    (
        [(
            header::CONTENT_TYPE,
            "application/javascript; charset=utf-8",
        )],
        include_bytes!("../../web/vendor/livekit-client.umd.js").as_slice(),
    )
        .into_response()
}

async fn preview(
    State(state): State<Arc<AppState>>,
    Path(id): Path<Uuid>,
) -> ApiResult<Json<chat_protocol::VoiceRoom>> {
    Ok(Json(service::preview(&state, id).await?))
}

async fn guest(
    State(state): State<Arc<AppState>>,
    Path(id): Path<Uuid>,
    headers: HeaderMap,
    ClientIp(ip): ClientIp,
    Json(body): Json<chat_protocol::VoiceGuestRequest>,
) -> ApiResult<Json<chat_protocol::VoiceGuestSession>> {
    if !state
        .limiter
        .check(&format!("voice-guest:{ip}"), 8, Duration::from_secs(60))
        .await
    {
        return Err(ApiErr::too_many());
    }
    Ok(Json(
        service::join(&state, id, body, advertised_rtc(&state, &headers)).await?,
    ))
}
