mod devices;
mod engine;

use serde_json::{Value, json};
use std::io::{self, BufRead, Write};

fn main() {
    let stdin = io::stdin();
    let mut input_device: Option<String> = None;
    let mut output_device: Option<String> = None;
    let mut muted = false;
    let mut deafened = false;
    let mut channels: u16 = 2;
    let mut loopback: Option<engine::Loopback> = None;
    for line in stdin.lock().lines() {
        let Ok(line) = line else { break };
        if line.trim().is_empty() {
            continue;
        }
        let request: Value = serde_json::from_str(&line).unwrap_or(Value::Null);
        let id = request.get("id").cloned().unwrap_or(Value::from(0));
        let op = request
            .get("op")
            .and_then(Value::as_str)
            .unwrap_or_default();
        let reply = match op {
            "join" => {
                loopback = None;
                match apply_session(
                    &request,
                    &mut input_device,
                    &mut output_device,
                    &mut muted,
                    &mut deafened,
                    &mut channels,
                    None,
                ) {
                    Ok(()) => json!({
                        "id": id,
                        "ok": false,
                        "error": "LiveKit native client is not linked. Rebuild chat-media-worker with --features livekit after LiveKit is running."
                    }),
                    Err(error) => json!({ "id": id, "ok": false, "error": error }),
                }
            }
            "devices" => match devices::list() {
                Ok(list) => json!({
                    "id": id,
                    "ok": true,
                    "inputs": list["inputs"],
                    "outputs": list["outputs"]
                }),
                Err(error) => json!({ "id": id, "ok": false, "error": error }),
            },
            "device" => match apply_session(
                &request,
                &mut input_device,
                &mut output_device,
                &mut muted,
                &mut deafened,
                &mut channels,
                loopback.as_mut(),
            ) {
                Ok(()) => match restart_loopback(
                    &mut loopback,
                    input_device.as_deref(),
                    output_device.as_deref(),
                    channels,
                    muted,
                    deafened,
                ) {
                    Ok(()) => json!({
                        "id": id,
                        "ok": true,
                        "input_device": input_device,
                        "output_device": output_device
                    }),
                    Err(error) => json!({ "id": id, "ok": false, "error": error }),
                },
                Err(error) => json!({ "id": id, "ok": false, "error": error }),
            },
            "mute" => {
                muted = request
                    .get("muted")
                    .and_then(Value::as_bool)
                    .unwrap_or(muted);
                if let Some(engine) = &loopback {
                    engine.set_mute(muted);
                }
                json!({"id": id, "ok": true})
            }
            "deafen" => {
                deafened = request
                    .get("deafened")
                    .and_then(Value::as_bool)
                    .unwrap_or(deafened);
                if let Some(engine) = &loopback {
                    engine.set_deaf(deafened);
                }
                json!({"id": id, "ok": true})
            }
            "quality" => match apply_session(
                &request,
                &mut input_device,
                &mut output_device,
                &mut muted,
                &mut deafened,
                &mut channels,
                loopback.as_mut(),
            ) {
                Ok(()) => match restart_loopback(
                    &mut loopback,
                    input_device.as_deref(),
                    output_device.as_deref(),
                    channels,
                    muted,
                    deafened,
                ) {
                    Ok(()) => json!({
                        "id": id,
                        "ok": true,
                        "jitter_frames": engine::JITTER_FRAMES,
                        "frame_ms": engine::FRAME_MS
                    }),
                    Err(error) => json!({ "id": id, "ok": false, "error": error }),
                },
                Err(error) => json!({ "id": id, "ok": false, "error": error }),
            },
            "loopback" => {
                loopback = None;
                match apply_session(
                    &request,
                    &mut input_device,
                    &mut output_device,
                    &mut muted,
                    &mut deafened,
                    &mut channels,
                    None,
                ) {
                    Ok(()) => match engine::start(
                        input_device.as_deref(),
                        output_device.as_deref(),
                        channels,
                        muted,
                        deafened,
                    ) {
                        Ok(engine) => {
                            loopback = Some(engine);
                            json!({
                                "id": id,
                                "ok": true,
                                "frame_ms": engine::FRAME_MS,
                                "jitter_frames": engine::JITTER_FRAMES
                            })
                        }
                        Err(error) => json!({ "id": id, "ok": false, "error": error }),
                    },
                    Err(error) => json!({ "id": id, "ok": false, "error": error }),
                }
            }
            "loopback_stop" | "leave" => {
                loopback = None;
                json!({"id": id, "ok": true})
            }
            "shutdown" => {
                drop(loopback);
                let _ = writeln!(io::stdout(), "{}", json!({"id": id, "ok": true}));
                return;
            }
            _ => json!({"id": id, "ok": false, "error": "unknown op"}),
        };
        if writeln!(io::stdout(), "{reply}").is_err() {
            break;
        }
        let _ = io::stdout().flush();
    }
}

fn apply_session(
    request: &Value,
    input_device: &mut Option<String>,
    output_device: &mut Option<String>,
    muted: &mut bool,
    deafened: &mut bool,
    channels: &mut u16,
    loopback: Option<&mut engine::Loopback>,
) -> Result<(), String> {
    if let Some(value) = request.get("muted").and_then(Value::as_bool) {
        *muted = value;
    }
    if let Some(value) = request.get("deafened").and_then(Value::as_bool) {
        *deafened = value;
    }
    if let Some(value) = request.get("channels").and_then(Value::as_u64) {
        *channels = value.clamp(1, 2) as u16;
    }
    let input = request.get("input_device").and_then(Value::as_str);
    let output = request.get("output_device").and_then(Value::as_str);
    devices::validate("in", input)?;
    devices::validate("out", output)?;
    let mut route_changed = false;
    if request.get("input_device").is_some() {
        let next = input
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .map(str::to_string);
        route_changed |= next != *input_device;
        *input_device = next;
    }
    if request.get("output_device").is_some() {
        let next = output
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .map(str::to_string);
        route_changed |= next != *output_device;
        *output_device = next;
    }
    if let Some(engine) = loopback {
        engine.set_mute(*muted);
        engine.set_deaf(*deafened);
        if route_changed {
            devices::invalidate();
        }
    }
    Ok(())
}

fn restart_loopback(
    loopback: &mut Option<engine::Loopback>,
    input_device: Option<&str>,
    output_device: Option<&str>,
    channels: u16,
    muted: bool,
    deafened: bool,
) -> Result<(), String> {
    if loopback.is_none() {
        return Ok(());
    }
    *loopback = None;
    *loopback = Some(engine::start(
        input_device,
        output_device,
        channels,
        muted,
        deafened,
    )?);
    Ok(())
}
