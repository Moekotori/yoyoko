use serde_json::{Value, json};
use std::io::{self, BufRead, Write};

fn main() {
    let stdin = io::stdin();
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
            "join" => json!({
                "id": id,
                "ok": false,
                "error": "LiveKit native client is not linked. Rebuild chat-media-worker with --features livekit after LiveKit is running."
            }),
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
