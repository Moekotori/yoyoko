use crate::configuration::Rtc;
use base64::Engine;
use base64::engine::general_purpose::URL_SAFE_NO_PAD;
use hmac::{Hmac, Mac};
use serde::Serialize;
use sha2::Sha256;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

type HmacSha256 = Hmac<Sha256>;

#[derive(Serialize)]
struct VideoGrant {
    #[serde(rename = "roomJoin")]
    room_join: bool,
    room: String,
    #[serde(rename = "canPublish")]
    can_publish: bool,
    #[serde(rename = "canSubscribe")]
    can_subscribe: bool,
    #[serde(rename = "canPublishData")]
    can_publish_data: bool,
    #[serde(rename = "canPublishSources")]
    can_publish_sources: Vec<&'static str>,
}

#[derive(Serialize)]
struct LiveKitClaims<'a> {
    iss: &'a str,
    sub: &'a str,
    name: &'a str,
    nbf: u64,
    exp: u64,
    video: VideoGrant,
}

pub struct IssuedToken {
    pub jwt: String,
    pub url: String,
    pub room: String,
    pub expires_at_unix: u64,
}

pub fn mint_voice_token(
    rtc: &Rtc,
    identity: &str,
    name: &str,
    room: &str,
    can_publish: bool,
    ttl: Duration,
) -> Result<IssuedToken, &'static str> {
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    let exp = now + ttl.as_secs().max(30);
    let header = serde_json::json!({"alg":"HS256","typ":"JWT","kid": rtc.api_key});
    let claims = LiveKitClaims {
        iss: &rtc.api_key,
        sub: identity,
        name,
        nbf: now.saturating_sub(5),
        exp,
        video: VideoGrant {
            room_join: true,
            room: room.to_string(),
            can_publish,
            can_subscribe: true,
            can_publish_data: true,
            can_publish_sources: if can_publish {
                vec!["microphone"]
            } else {
                vec![]
            },
        },
    };
    let header_b64 = URL_SAFE_NO_PAD.encode(header.to_string());
    let payload_b64 =
        URL_SAFE_NO_PAD.encode(serde_json::to_vec(&claims).map_err(|_| "token encode")?);
    let signing = format!("{header_b64}.{payload_b64}");
    let mut mac = HmacSha256::new_from_slice(rtc.api_secret.as_bytes()).map_err(|_| "token key")?;
    mac.update(signing.as_bytes());
    let jwt = format!(
        "{signing}.{}",
        URL_SAFE_NO_PAD.encode(mac.finalize().into_bytes())
    );
    Ok(IssuedToken {
        jwt,
        url: websocket_url(&rtc.public_url),
        room: room.to_string(),
        expires_at_unix: exp,
    })
}

pub fn websocket_url(public_url: &str) -> String {
    public_url
        .trim_end_matches('/')
        .replacen("https://", "wss://", 1)
        .replacen("http://", "ws://", 1)
}

pub fn rfc3339_unix(ts: u64) -> String {
    let secs = i64::try_from(ts).unwrap_or(0);
    chrono::DateTime::from_timestamp(secs, 0)
        .unwrap_or(chrono::DateTime::UNIX_EPOCH)
        .to_rfc3339_opts(chrono::SecondsFormat::Secs, true)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mints_livekit_grants() {
        let rtc = Rtc {
            provider: "livekit".into(),
            public_url: "http://localhost:7880".into(),
            api_key: "devkey".into(),
            api_secret: "local-development-only-change-me-32".into(),
        };
        let token = mint_voice_token(
            &rtc,
            "01950000-0000-7000-8000-000000000030",
            "Ada",
            "voice:room",
            true,
            Duration::from_secs(60),
        )
        .unwrap();
        assert_eq!(token.url, "ws://localhost:7880");
        let parts: Vec<_> = token.jwt.split('.').collect();
        assert_eq!(parts.len(), 3);
        let payload = String::from_utf8(URL_SAFE_NO_PAD.decode(parts[1]).unwrap()).unwrap();
        assert!(payload.contains("\"roomJoin\":true"));
        assert!(payload.contains("\"canPublish\":true"));
        assert!(payload.contains("voice:room"));
        let mut mac = HmacSha256::new_from_slice(rtc.api_secret.as_bytes()).unwrap();
        mac.update(format!("{}.{}", parts[0], parts[1]).as_bytes());
        let expected = URL_SAFE_NO_PAD.encode(mac.finalize().into_bytes());
        assert_eq!(expected, parts[2]);
    }
}
