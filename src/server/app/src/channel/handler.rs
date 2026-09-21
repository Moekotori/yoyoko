use crate::{api::Auth, error::ApiResult, state::AppState};
use axum::{
    Json,
    extract::{Path, State},
    http::StatusCode,
};
use std::sync::Arc;
use uuid::Uuid;

pub async fn patch(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
    Json(body): Json<chat_protocol::PatchChannelRequest>,
) -> ApiResult<Json<chat_protocol::Channel>> {
    Ok(Json(
        super::service::update(&state, user, id, body.name, body.audio_quality).await?,
    ))
}
pub async fn delete(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Path(id): Path<Uuid>,
) -> ApiResult<StatusCode> {
    super::service::delete(&state, user, id).await?;
    Ok(StatusCode::NO_CONTENT)
}
