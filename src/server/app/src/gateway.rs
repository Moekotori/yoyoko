use crate::{services, state::AppState};
use axum::{
    extract::{
        State, WebSocketUpgrade,
        ws::{CloseFrame, Message, WebSocket},
    },
    response::Response,
};
use chat_protocol::{GatewayEnvelope, Identify, MAX_GATEWAY_BYTES, PROTOCOL_VERSION, Resume};
use chrono::Utc;
use serde_json::json;
use std::{
    sync::Arc,
    time::{Duration, Instant},
};
use uuid::Uuid;

pub async fn upgrade(ws: WebSocketUpgrade, State(state): State<Arc<AppState>>) -> Response {
    ws.max_message_size(MAX_GATEWAY_BYTES)
        .max_frame_size(MAX_GATEWAY_BYTES)
        .on_upgrade(move |socket| session(socket, state))
}

async fn session(mut socket: WebSocket, state: Arc<AppState>) {
    let hello = GatewayEnvelope {
        op: "hello".into(),
        event: None,
        seq: None,
        data: json!({"protocol_version":PROTOCOL_VERSION,"heartbeat_interval_ms":30000}),
    };
    if !send(&mut socket, &hello).await {
        return;
    }
    let frame = tokio::time::timeout(Duration::from_secs(5), socket.recv()).await;
    let identified = match frame {
        Ok(Some(Ok(Message::Text(text)))) => match serde_json::from_str::<GatewayEnvelope>(&text) {
            Ok(envelope) if envelope.op == "identify" => identify(&state, envelope.data).await,
            Ok(envelope) if envelope.op == "resume" => resume(&state, envelope.data).await,
            Ok(_) => Err((4400, "Expected identify or resume")),
            Err(_) => Err((4400, "Malformed envelope")),
        },
        _ => Err((4408, "Handshake timeout or closed connection")),
    };
    let (user, session_id, replay) = match identified {
        Ok(value) => value,
        Err((code, reason)) => {
            close(&mut socket, code, reason).await;
            return;
        }
    };
    let ready = match services::ready_payload(&state, user, session_id).await {
        Ok(payload) => payload,
        Err(_) => {
            close(&mut socket, 4401, "Not authorized").await;
            return;
        }
    };
    let ready = GatewayEnvelope {
        op: "dispatch".into(),
        event: Some("READY".into()),
        seq: None,
        data: serde_json::to_value(ready).unwrap_or_else(|_| json!({})),
    };
    if !send(&mut socket, &ready).await {
        return;
    }
    for event in replay {
        let envelope = GatewayEnvelope {
            op: "dispatch".into(),
            event: Some(event.event),
            seq: Some(event.seq.to_string()),
            data: event.payload,
        };
        if !send(&mut socket, &envelope).await {
            return;
        }
    }
    let mut rx = state.hub.subscribe(user, session_id).await;
    let mut last_beat = Instant::now();
    let mut last_seq: i64 = 0;
    loop {
        tokio::select! {
            incoming = socket.recv() => {
                match incoming {
                    Some(Ok(Message::Text(text))) => {
                        if let Ok(envelope) = serde_json::from_str::<GatewayEnvelope>(&text)
                            && envelope.op == "heartbeat"
                        {
                            last_beat = Instant::now();
                            let ack = GatewayEnvelope {
                                op: "heartbeat_ack".into(),
                                event: None,
                                seq: None,
                                data: json!({}),
                            };
                            if !send(&mut socket, &ack).await {
                                break;
                            }
                        }
                    }
                    Some(Ok(Message::Ping(payload))) => {
                        let _ = socket.send(Message::Pong(payload)).await;
                    }
                    Some(Ok(Message::Close(_))) | None | Some(Err(_)) => break,
                    _ => {}
                }
            }
            event = rx.recv() => {
                match event {
                    Some(envelope) => {
                        if let Some(seq) = envelope.seq.as_deref().and_then(|s| s.parse().ok()) {
                            last_seq = seq;
                        }
                        if !send(&mut socket, envelope.as_ref()).await {
                            break;
                        }
                    }
                    None => break,
                }
            }
            _ = tokio::time::sleep(Duration::from_secs(5)) => {
                if last_beat.elapsed() > Duration::from_secs(60) {
                    close(&mut socket, 4408, "Heartbeat timeout").await;
                    break;
                }
            }
        }
    }
    state.hub.unsubscribe(user, session_id).await;
    let _ = state
        .store
        .touch_gateway_session(session_id, last_seq, Utc::now().timestamp() + 15 * 60)
        .await;
    let state = Arc::clone(&state);
    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_secs(15)).await;
        if !state.hub.is_connected(user).await {
            let _ = crate::services::leave_voice(&state, user).await;
        }
    });
}

async fn identify(
    state: &AppState,
    data: serde_json::Value,
) -> Result<(Uuid, Uuid, Vec<crate::store::OutboxEvent>), (u16, &'static str)> {
    let identify: Identify =
        serde_json::from_value(data).map_err(|_| (4400, "Invalid identify payload"))?;
    if identify.protocol_version != PROTOCOL_VERSION {
        return Err((4406, "Incompatible protocol version"));
    }
    let claims = state
        .tokens
        .verify_access(&identify.access_token)
        .ok_or((4401, "Invalid access token"))?;
    let session = state
        .store
        .create_gateway_session(
            Uuid::now_v7(),
            claims.user_id,
            Utc::now().timestamp() + 15 * 60,
        )
        .await
        .map_err(|_| (1011, "Session unavailable"))?;
    Ok((claims.user_id, session.id, vec![]))
}

async fn resume(
    state: &AppState,
    data: serde_json::Value,
) -> Result<(Uuid, Uuid, Vec<crate::store::OutboxEvent>), (u16, &'static str)> {
    let resume: Resume =
        serde_json::from_value(data).map_err(|_| (4400, "Invalid resume payload"))?;
    if resume.protocol_version != PROTOCOL_VERSION {
        return Err((4406, "Incompatible protocol version"));
    }
    let claims = state
        .tokens
        .verify_access(&resume.access_token)
        .ok_or((4401, "Invalid access token"))?;
    let session_id: Uuid = resume
        .session_id
        .parse()
        .map_err(|_| (4400, "Invalid session"))?;
    let session = state
        .store
        .find_gateway_session(session_id)
        .await
        .map_err(|_| (1011, "Session unavailable"))?
        .ok_or((4409, "Invalid session"))?;
    if session.user_id != claims.user_id || session.expires_at < Utc::now().timestamp() {
        return Err((4409, "Invalid session"));
    }
    let last_seq = resume.last_seq.parse().unwrap_or(0);
    let replay = state
        .store
        .replay(claims.user_id, last_seq, 10_000)
        .await
        .map_err(|_| (1011, "Replay unavailable"))?;
    Ok((claims.user_id, session.id, replay))
}

async fn send(socket: &mut WebSocket, envelope: &GatewayEnvelope) -> bool {
    let Ok(text) = serde_json::to_string(envelope) else {
        return false;
    };
    socket.send(Message::Text(text.into())).await.is_ok()
}

async fn close(socket: &mut WebSocket, code: u16, reason: &'static str) {
    if code == 4409 {
        let _ = send(
            socket,
            &GatewayEnvelope {
                op: "invalid_session".into(),
                event: None,
                seq: None,
                data: json!({"reason": reason}),
            },
        )
        .await;
    }
    let _ = tokio::time::timeout(
        Duration::from_secs(1),
        socket.send(Message::Close(Some(CloseFrame {
            code,
            reason: reason.into(),
        }))),
    )
    .await;
}
