use crate::{api::Auth, error::ApiResult, state::AppState};
use axum::{
    Json,
    extract::State,
};
use std::sync::Arc;

pub async fn list(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
) -> ApiResult<Json<Vec<chat_protocol::Channel>>> {
    Ok(Json(super::service::list(&state, user).await?))
}

pub async fn open(
    State(state): State<Arc<AppState>>,
    Auth(user, _): Auth,
    Json(body): Json<chat_protocol::OpenDmRequest>,
) -> ApiResult<Json<chat_protocol::Channel>> {
    Ok(Json(
        super::service::open(&state, user, body.recipient_id).await?,
    ))
}
