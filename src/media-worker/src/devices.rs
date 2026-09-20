use cpal::traits::{DeviceTrait, HostTrait};
use serde_json::{Value, json};
use std::collections::HashMap;

pub fn list() -> Result<Value, String> {
    let (inputs, outputs) = enumerate()?;
    Ok(json!({ "inputs": inputs, "outputs": outputs }))
}

pub fn validate(kind: &str, id: Option<&str>) -> Result<(), String> {
    let Some(id) = id.map(str::trim).filter(|value| !value.is_empty()) else {
        return Ok(());
    };
    let (inputs, outputs) = enumerate()?;
    let list = if kind == "in" { &inputs } else { &outputs };
    if list.iter().any(|device| device["id"] == id) {
        Ok(())
    } else {
        Err(format!("unknown_{kind}put_device"))
    }
}

fn enumerate() -> Result<(Vec<Value>, Vec<Value>), String> {
    let host = cpal::default_host();
    let inputs = collect(
        host.input_devices()
            .map_err(|err| err.to_string())?,
        host.default_input_device(),
        "in",
    )?;
    let outputs = collect(
        host.output_devices()
            .map_err(|err| err.to_string())?,
        host.default_output_device(),
        "out",
    )?;
    Ok((inputs, outputs))
}

fn collect(
    devices: impl Iterator<Item = cpal::Device>,
    default: Option<cpal::Device>,
    prefix: &str,
) -> Result<Vec<Value>, String> {
    let default_name = default.and_then(|device| device.name().ok());
    let mut seen: HashMap<String, u32> = HashMap::new();
    let mut out = Vec::new();
    for device in devices {
        let name = device.name().unwrap_or_else(|_| "Unknown".into());
        let count = seen.entry(name.clone()).or_insert(0);
        *count += 1;
        let id = if *count == 1 {
            format!("{prefix}:{name}")
        } else {
            format!("{prefix}:{name}:{count}")
        };
        let label = if *count == 1 {
            name.clone()
        } else {
            format!("{name} ({count})")
        };
        let is_default = default_name.as_ref() == Some(&name) && *count == 1;
        out.push(json!({ "id": id, "name": label, "default": is_default }));
        if out.len() >= 64 {
            break;
        }
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn list_returns_object_or_error() {
        match list() {
            Ok(value) => {
                assert!(value.get("inputs").and_then(Value::as_array).is_some());
                assert!(value.get("outputs").and_then(Value::as_array).is_some());
            }
            Err(err) => assert!(!err.is_empty()),
        }
    }
}
