use crate::configuration::Settings;
use axum::{
    Json, Router,
    extract::State,
    http::{HeaderValue, StatusCode},
    routing::get,
};
use chat_protocol::{API_VERSION, ApiError, InstanceDiscovery, PROTOCOL_VERSION};
use std::sync::Arc;
use tower_http::{
    cors::CorsLayer, limit::RequestBodyLimitLayer, timeout::TimeoutLayer, trace::TraceLayer,
};

pub fn router(settings: Settings) -> Router {
    let origins: Vec<HeaderValue> = settings
        .server
        .allowed_origins
        .iter()
        .filter_map(|origin| origin.parse().ok())
        .collect();
    Router::new()
        .route(
            "/health/live",
            get(|| async { Json(serde_json::json!({"status":"ok", "phase":0})) }),
        )
        .route("/.well-known/lightchat", get(discovery))
        .route("/gateway", get(crate::gateway::upgrade))
        .fallback(|| async {
            (
                StatusCode::NOT_FOUND,
                Json(ApiError {
                    code: "not_found".into(),
                    message: "No such endpoint. Business APIs are not implemented yet.".into(),
                }),
            )
        })
        .with_state(Arc::new(settings))
        .layer(
            CorsLayer::new()
                .allow_origin(origins)
                .allow_methods([axum::http::Method::GET]),
        )
        .layer(RequestBodyLimitLayer::new(64 * 1024))
        .layer(TimeoutLayer::with_status_code(
            StatusCode::REQUEST_TIMEOUT,
            std::time::Duration::from_secs(10),
        ))
        .layer(TraceLayer::new_for_http())
}
async fn discovery(State(settings): State<Arc<Settings>>) -> Json<InstanceDiscovery> {
    let origin = settings.server.public_url.trim_end_matches('/');
    let gateway = origin
        .replacen("https://", "wss://", 1)
        .replacen("http://", "ws://", 1);
    Json(InstanceDiscovery {
        instance_id: settings.server.instance_id,
        name: settings.server.name.clone(),
        protocol_version: PROTOCOL_VERSION,
        api_version: API_VERSION,
        api: format!("{origin}/api/v1"),
        gateway: format!("{gateway}/gateway"),
        cdn: settings.storage.public_url.clone(),
        rtc: settings.rtc.public_url.clone(),
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::{
        body::{Body, to_bytes},
        http::Request,
    };
    use tower::ServiceExt;
    #[tokio::test]
    async fn discovery_is_versioned_and_business_routes_are_closed() {
        let config =
            Settings::load(concat!(env!("CARGO_MANIFEST_DIR"), "/../../../config.toml")).unwrap();
        let app = router(config);
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
        let body = to_bytes(response.into_body(), 16384).await.unwrap();
        let info: InstanceDiscovery = serde_json::from_slice(&body).unwrap();
        assert_eq!(info.protocol_version, 1);
        assert_eq!(info.gateway, "ws://localhost:8080/gateway");
        let response = app
            .oneshot(
                Request::builder()
                    .uri("/api/v1/messages")
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::NOT_FOUND);
    }
}
