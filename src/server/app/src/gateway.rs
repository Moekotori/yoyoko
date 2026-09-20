use axum::{
    extract::WebSocketUpgrade,
    extract::ws::{CloseFrame, Message, WebSocket},
    response::Response,
};
use chat_protocol::{GatewayEnvelope, Identify, MAX_GATEWAY_BYTES, PROTOCOL_VERSION};
use serde_json::json;
use std::time::Duration;

pub async fn upgrade(ws: WebSocketUpgrade) -> Response {
    ws.max_message_size(MAX_GATEWAY_BYTES)
        .max_frame_size(MAX_GATEWAY_BYTES)
        .on_upgrade(session)
}
async fn session(mut socket: WebSocket) {
    let hello = GatewayEnvelope {
        op: "hello".into(),
        event: None,
        seq: None,
        data: json!({"protocol_version":PROTOCOL_VERSION,"heartbeat_interval_ms":30000}),
    };
    if socket
        .send(Message::Text(serde_json::to_string(&hello).unwrap().into()))
        .await
        .is_err()
    {
        return;
    }
    let frame = tokio::time::timeout(Duration::from_secs(5), socket.recv()).await;
    let (code, reason) = match frame {
        Ok(Some(Ok(Message::Text(text)))) => match serde_json::from_str::<GatewayEnvelope>(&text) {
            Ok(envelope) if envelope.op == "identify" => {
                match serde_json::from_value::<Identify>(envelope.data) {
                    Ok(identify) if identify.protocol_version != PROTOCOL_VERSION => {
                        (4406, "Incompatible protocol version")
                    }
                    Ok(_) => (4401, "Not implemented yet: authentication"),
                    Err(_) => (4400, "Invalid identify payload"),
                }
            }
            Ok(_) => (4400, "Expected identify; resume not implemented yet"),
            Err(_) => (4400, "Malformed envelope"),
        },
        _ => (4408, "Handshake timeout or closed connection"),
    };
    let _ = tokio::time::timeout(
        Duration::from_secs(1),
        socket.send(Message::Close(Some(CloseFrame {
            code,
            reason: reason.into(),
        }))),
    )
    .await;
}
