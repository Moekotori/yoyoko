use axum::{
    Json,
    http::StatusCode,
    response::{IntoResponse, Response},
};
use chat_protocol::ApiError;

pub type ApiResult<T> = Result<T, ApiErr>;

pub struct ApiErr {
    pub status: StatusCode,
    pub code: &'static str,
    pub message: String,
}

impl ApiErr {
    pub fn new(status: StatusCode, code: &'static str, message: impl Into<String>) -> Self {
        Self {
            status,
            code,
            message: message.into(),
        }
    }
    pub fn unauthorized() -> Self {
        Self::new(
            StatusCode::UNAUTHORIZED,
            "unauthorized",
            "Authentication required.",
        )
    }
    pub fn forbidden() -> Self {
        Self::new(StatusCode::FORBIDDEN, "forbidden", "Missing permission.")
    }
    pub fn not_found() -> Self {
        Self::new(StatusCode::NOT_FOUND, "not_found", "Not found.")
    }
    pub fn conflict(message: impl Into<String>) -> Self {
        Self::new(StatusCode::CONFLICT, "conflict", message)
    }
    pub fn bad(code: &'static str, message: impl Into<String>) -> Self {
        Self::new(StatusCode::BAD_REQUEST, code, message)
    }
    pub fn too_many() -> Self {
        Self::new(
            StatusCode::TOO_MANY_REQUESTS,
            "rate_limited",
            "Too many requests.",
        )
    }
    pub fn unavailable() -> Self {
        Self::new(
            StatusCode::SERVICE_UNAVAILABLE,
            "unavailable",
            "Storage unavailable.",
        )
    }
}

impl IntoResponse for ApiErr {
    fn into_response(self) -> Response {
        (
            self.status,
            Json(ApiError {
                code: self.code.into(),
                message: self.message,
            }),
        )
            .into_response()
    }
}
