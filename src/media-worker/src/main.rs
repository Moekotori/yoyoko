mod devices;

use serde_json::{Value, json};
use std::io::{self, BufRead, Write};

fn main() {
    let stdin = io::stdin();
    let mut input_device: Option<String> = None;
    let mut output_device: Option<String> = None;
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
            "join" => match apply_route(&request, &mut input_device, &mut output_device) {
                Ok(()) => json!({
                    "id": id,
                    "ok": false,
                    "error": "LiveKit native client is not linked. Rebuild chat-media-worker with --features livekit after LiveKit is running."
                }),
                Err(error) => json!({ "id": id, "ok": false, "error": error }),
            },
            "devices" => match devices::list() {
                Ok(list) => json!({
                    "id": id,
                    "ok": true,
                    "inputs": list["inputs"],
                    "outputs": list["outputs"]
                }),
                Err(error) => json!({ "id": id, "ok": false, "error": error }),
            },
            "device" => match apply_route(&request, &mut input_device, &mut output_device) {
                Ok(()) => json!({
                    "id": id,
                    "ok": true,
                    "input_device": input_device,
                    "output_device": output_device
                }),
                Err(error) => json!({ "id": id, "ok": false, "error": error }),
            },
            "leave" | "mute" | "deafen" | "quality" => json!({"id": id, "ok": true}),
            "shutdown" => {
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

fn apply_route(
    request: &Value,
    input_device: &mut Option<String>,
    output_device: &mut Option<String>,
) -> Result<(), String> {
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
