use axum::{
    Json,
    http::StatusCode,
    response::{IntoResponse, Response},
};
pub type ApiResult<T> = Result<T, ApiErr>;

pub struct ApiErr {
    pub status: StatusCode,
    pub code: &'static str,
    pub message: String,
    pub retry_after_seconds: Option<u32>,
}

impl ApiErr {
    pub fn new(status: StatusCode, code: &'static str, message: impl Into<String>) -> Self {
        Self {
            status,
            code,
            message: message.into(),
            retry_after_seconds: None,
        }
    }
    pub fn cooldown(seconds: u64) -> Self {
        let seconds = seconds.max(1) as u32;
        Self {
            status: StatusCode::TOO_MANY_REQUESTS,
            code: "cooldown",
            message: format!("Wait {seconds} more second(s) before sending."),
            retry_after_seconds: Some(seconds),
        }
    }
    pub fn blocked_word() -> Self {
        Self::bad("blocked_word", "Message contains a blocked word.")
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
        let mut body = serde_json::json!({
            "code": self.code,
            "message": self.message,
        });
        if let Some(seconds) = self.retry_after_seconds {
            body["retry_after_seconds"] = seconds.into();
        }
        (self.status, Json(body)).into_response()
    }
}
