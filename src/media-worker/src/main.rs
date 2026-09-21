mod devices;
mod engine;
mod rtc;

use serde_json::{Value, json};
use std::io::{self, BufRead, Write};
use tokio::sync::mpsc;

#[tokio::main]
async fn main() {
    let _ = env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("warn"))
        .target(env_logger::Target::Stderr)
        .try_init();
    let (tx, mut rx) = mpsc::unbounded_channel::<String>();
    std::thread::spawn(move || {
        let stdin = io::stdin();
        for line in stdin.lock().lines() {
            let Ok(line) = line else { break };
            if line.trim().is_empty() {
                continue;
            }
            if tx.send(line).is_err() {
                break;
            }
        }
    });
    let mut input_device: Option<String> = None;
    let mut output_device: Option<String> = None;
    let mut muted = false;
    let mut deafened = false;
    let mut channels: u16 = 2;
    let mut bitrate_bps: u32 = 510_000;
    let mut dtx = false;
    let mut fec = true;
    let mut loopback: Option<engine::Loopback> = None;
    let mut session: Option<rtc::Session> = None;
    while let Some(line) = rx.recv().await {
        let request: Value = serde_json::from_str(&line).unwrap_or(Value::Null);
        let id = request.get("id").cloned().unwrap_or(Value::from(0));
        let op = request
            .get("op")
            .and_then(Value::as_str)
            .unwrap_or_default();
        let reply = match op {
            "join" => {
                loopback = None;
                if let Some(current) = session.take() {
                    current.close().await;
                }
                match apply_session(
                    &request,
                    &mut input_device,
                    &mut output_device,
                    &mut muted,
                    &mut deafened,
                    &mut channels,
                    &mut bitrate_bps,
                    &mut dtx,
                    &mut fec,
                ) {
                    Ok(()) => {
                        let url = request
                            .get("url")
                            .and_then(Value::as_str)
                            .unwrap_or_default()
                            .to_string();
                        let token = request
                            .get("token")
                            .and_then(Value::as_str)
                            .unwrap_or_default()
                            .to_string();
                        match rtc::Session::connect(rtc::JoinConfig {
                            url,
                            token,
                            input_device: input_device.clone(),
                            output_device: output_device.clone(),
                            muted,
                            deafened,
                            bitrate_bps,
                            dtx,
                            fec,
                        })
                        .await
                        {
                            Ok(connected) => {
                                session = Some(connected);
                                json!({
                                    "id": id,
                                    "ok": true,
                                    "frame_ms": engine::FRAME_MS,
                                    "jitter_frames": engine::JITTER_FRAMES
                                })
                            }
                            Err(error) => json!({ "id": id, "ok": false, "error": error }),
                        }
                    }
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
                &mut bitrate_bps,
                &mut dtx,
                &mut fec,
            ) {
                Ok(()) => {
                    let rtc_result = if let Some(current) = session.as_ref() {
                        current.set_devices(input_device.as_deref(), output_device.as_deref())
                    } else {
                        Ok(())
                    };
                    match rtc_result.and_then(|()| {
                        restart_loopback(
                            &mut loopback,
                            input_device.as_deref(),
                            output_device.as_deref(),
                            channels,
                            muted,
                            deafened,
                        )
                    }) {
                        Ok(()) => json!({
                            "id": id,
                            "ok": true,
                            "input_device": input_device,
                            "output_device": output_device
                        }),
                        Err(error) => json!({ "id": id, "ok": false, "error": error }),
                    }
                }
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
                if let Some(current) = session.as_ref() {
                    current.set_mute(muted);
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
                if let Some(current) = session.as_ref() {
                    current.set_deaf(deafened);
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
                &mut bitrate_bps,
                &mut dtx,
                &mut fec,
            ) {
                Ok(()) => {
                    let rtc_result = if let Some(current) = session.as_mut() {
                        current.set_quality(bitrate_bps, dtx, fec).await
                    } else {
                        Ok(())
                    };
                    match rtc_result.and_then(|()| {
                        restart_loopback(
                            &mut loopback,
                            input_device.as_deref(),
                            output_device.as_deref(),
                            channels,
                            muted,
                            deafened,
                        )
                    }) {
                        Ok(()) => json!({
                            "id": id,
                            "ok": true,
                            "jitter_frames": engine::JITTER_FRAMES,
                            "frame_ms": engine::FRAME_MS
                        }),
                        Err(error) => json!({ "id": id, "ok": false, "error": error }),
                    }
                }
                Err(error) => json!({ "id": id, "ok": false, "error": error }),
            },
            "loopback" => {
                if let Some(current) = session.take() {
                    current.close().await;
                }
                loopback = None;
                match apply_session(
                    &request,
                    &mut input_device,
                    &mut output_device,
                    &mut muted,
                    &mut deafened,
                    &mut channels,
                    &mut bitrate_bps,
                    &mut dtx,
                    &mut fec,
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
                if let Some(current) = session.take() {
                    current.close().await;
                }
                json!({"id": id, "ok": true})
            }
            "shutdown" => {
                drop(loopback);
                if let Some(current) = session.take() {
                    current.close().await;
                }
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
    bitrate_bps: &mut u32,
    dtx: &mut bool,
    fec: &mut bool,
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
    if let Some(value) = request.get("bitrate_bps").and_then(Value::as_u64) {
        *bitrate_bps = value.clamp(16_000, 510_000) as u32;
    }
    if let Some(value) = request.get("dtx").and_then(Value::as_bool) {
        *dtx = value;
    }
    if let Some(value) = request.get("fec").and_then(Value::as_bool) {
        *fec = value;
    }
    let input = request.get("input_device").and_then(Value::as_str);
    let output = request.get("output_device").and_then(Value::as_str);
    devices::validate("in", input)?;
    devices::validate("out", output)?;
    if request.get("input_device").is_some() {
        *input_device = input
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .map(str::to_string);
    }
    if request.get("output_device").is_some() {
        *output_device = output
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .map(str::to_string);
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
