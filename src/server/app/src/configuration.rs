use serde::Deserialize;
use std::net::SocketAddr;
use url::Url;
use uuid::Uuid;

#[derive(Clone, Deserialize)]
pub struct Settings {
    pub server: Server,
    pub auth: Auth,
    pub database: Database,
    pub redis: Redis,
    pub storage: Storage,
    pub rtc: Rtc,
    pub logging: Logging,
}
#[derive(Clone, Deserialize)]
pub struct Server {
    pub instance_id: Uuid,
    pub name: String,
    pub listen: SocketAddr,
    pub public_url: String,
    pub allowed_origins: Vec<String>,
}
#[derive(Clone, Deserialize)]
pub struct Auth {
    pub token_secret: String,
    pub access_ttl_seconds: u64,
    pub refresh_ttl_days: u64,
    #[serde(default)]
    pub registration_password: Option<String>,
}
#[derive(Clone, Deserialize)]
pub struct Database {
    pub url: String,
    pub max_connections: u32,
}
#[derive(Clone, Deserialize)]
pub struct Redis {
    pub url: String,
}
#[derive(Clone, Deserialize)]
pub struct Storage {
    pub provider: String,
    pub endpoint: String,
    pub public_url: String,
    pub bucket: String,
    pub access_key: String,
    pub secret_key: String,
    #[serde(default = "default_local_dir")]
    pub local_dir: String,
    #[serde(default = "default_max_bytes")]
    pub max_bytes: u64,
}
fn default_local_dir() -> String {
    "data/objects".into()
}
fn default_max_bytes() -> u64 {
    chat_protocol::MAX_ATTACHMENT_BYTES
}
#[derive(Clone, Deserialize)]
pub struct Rtc {
    pub provider: String,
    pub public_url: String,
    pub api_key: String,
    pub api_secret: String,
}
#[derive(Clone, Deserialize)]
pub struct Logging {
    pub filter: String,
}

impl Settings {
    pub fn load(path: &str) -> Result<Self, Box<dyn std::error::Error>> {
        let result: Self = config::Config::builder()
            .add_source(config::File::with_name(path))
            .add_source(
                config::Environment::with_prefix("CHAT")
                    .prefix_separator("__")
                    .separator("__")
                    .try_parsing(true)
                    .list_separator(",")
                    .with_list_parse_key("server.allowed_origins"),
            )
            .build()?
            .try_deserialize()?;
        result.validate()?;
        Ok(result)
    }
    pub fn validate(&self) -> Result<(), Box<dyn std::error::Error>> {
        let public = Url::parse(&self.server.public_url)?;
        if !(public.scheme() == "https"
            || (public.scheme() == "http" && is_local_dev_host(public.host_str())))
            || public.path() != "/"
            || public.query().is_some()
            || public.fragment().is_some()
            || !public.username().is_empty()
            || public.password().is_some()
        {
            return Err(
                "server.public_url must be an HTTPS origin (HTTP allowed on loopback and LAN)"
                    .into(),
            );
        }
        if self.server.instance_id.is_nil()
            || self.server.name.trim().is_empty()
            || self.server.name.len() > 100
        {
            return Err("invalid instance identity".into());
        }
        if !(1..=32).contains(&self.database.max_connections) {
            return Err("database pool must be 1..32".into());
        }
        let db = &self.database.url;
        if !(db.starts_with("postgres://")
            || db.starts_with("postgresql://")
            || db.starts_with("local:"))
        {
            return Err("database.url must be postgres:// or local:path".into());
        }
        if self.auth.token_secret.len() < 16
            || !(60..=86400).contains(&self.auth.access_ttl_seconds)
            || !(1..=90).contains(&self.auth.refresh_ttl_days)
        {
            return Err("invalid auth token settings".into());
        }
        if self
            .auth
            .registration_password
            .as_ref()
            .is_some_and(|password| password.is_empty() || password.len() > 128)
        {
            return Err("auth.registration_password must contain 1..=128 bytes when configured".into());
        }
        for endpoint in [&self.storage.public_url, &self.rtc.public_url] {
            let uri = Url::parse(endpoint)?;
            if !(uri.scheme() == "https"
                || (uri.scheme() == "http" && is_local_dev_host(uri.host_str())))
            {
                return Err(
                    "public storage/RTC URLs must use HTTPS outside local development".into(),
                );
            }
        }
        if !matches!(self.storage.provider.as_str(), "local" | "s3")
            || self.rtc.provider != "livekit"
        {
            return Err("unsupported storage/RTC provider".into());
        }
        if self.storage.local_dir.trim().is_empty() {
            return Err("storage.local_dir required".into());
        }
        if !(64 * 1024..=chat_protocol::MAX_ATTACHMENT_BYTES_CEILING)
            .contains(&self.storage.max_bytes)
        {
            return Err("storage.max_bytes must be 64 KiB..=256 MiB".into());
        }
        Ok(())
    }
    pub fn api_origin(&self) -> String {
        format!("{}/api/v1", self.server.public_url.trim_end_matches('/'))
    }
}

pub(crate) fn is_local_dev_host(host: Option<&str>) -> bool {
    let Some(host) = host else {
        return false;
    };
    if host.eq_ignore_ascii_case("localhost") {
        return true;
    }
    let Ok(ip) = host.parse::<std::net::IpAddr>() else {
        return false;
    };
    match ip {
        std::net::IpAddr::V4(v4) => {
            !v4.is_unspecified() && (v4.is_loopback() || v4.is_private() || v4.is_link_local())
        }
        std::net::IpAddr::V6(v6) => {
            !v6.is_unspecified()
                && (v6.is_loopback() || v6.is_unique_local() || v6.is_unicast_link_local())
        }
    }
}

fn is_loopback_host(host: Option<&str>) -> bool {
    let Some(host) = host else {
        return false;
    };
    if host.eq_ignore_ascii_case("localhost") {
        return true;
    }
    host.parse::<std::net::IpAddr>()
        .is_ok_and(|ip| ip.is_loopback())
}

pub(crate) fn advertised_rtc_url(rtc_public_url: &str, host_header: Option<&str>) -> String {
    let fallback = rtc_public_url.trim_end_matches('/').to_string();
    let Ok(rtc) = Url::parse(&fallback) else {
        return fallback;
    };
    if !is_loopback_host(rtc.host_str()) {
        return fallback;
    }
    let Some(request_host) = host_header.map(str::trim).filter(|value| !value.is_empty()) else {
        return fallback;
    };
    if request_host.contains('/') || request_host.contains('@') || request_host.len() > 255 {
        return fallback;
    }
    let Ok(request) = Url::parse(&format!("http://{request_host}")) else {
        return fallback;
    };
    if !is_local_dev_host(request.host_str()) || is_loopback_host(request.host_str()) {
        return fallback;
    }
    let Some(lan_host) = request.host_str() else {
        return fallback;
    };
    match rtc.port_or_known_default() {
        Some(port) => format!("{}://{lan_host}:{port}", rtc.scheme()),
        None => format!("{}://{lan_host}", rtc.scheme()),
    }
}

pub(crate) fn advertised_origin(public_url: &str, host_header: Option<&str>) -> String {
    let fallback = public_url.trim_end_matches('/').to_string();
    let Some(host) = host_header.map(str::trim).filter(|value| !value.is_empty()) else {
        return fallback;
    };
    if host.contains('/') || host.contains('@') || host.len() > 255 {
        return fallback;
    }
    let Ok(url) = Url::parse(&format!("http://{host}")) else {
        return fallback;
    };
    if !is_local_dev_host(url.host_str()) {
        return fallback;
    }
    format!("http://{host}")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn local_dev_hosts_are_private_or_loopback() {
        assert!(is_local_dev_host(Some("localhost")));
        assert!(is_local_dev_host(Some("127.0.0.1")));
        assert!(is_local_dev_host(Some("192.168.1.10")));
        assert!(is_local_dev_host(Some("10.0.0.2")));
        assert!(is_local_dev_host(Some("172.16.5.1")));
        assert!(!is_local_dev_host(Some("8.8.8.8")));
        assert!(!is_local_dev_host(Some("example.com")));
        assert!(!is_local_dev_host(Some("0.0.0.0")));
    }

    #[test]
    fn discovery_origin_reflects_lan_host_header() {
        assert_eq!(
            advertised_origin("http://localhost:8080", Some("192.168.1.10:8080")),
            "http://192.168.1.10:8080"
        );
        assert_eq!(
            advertised_origin("http://localhost:8080", Some("example.com")),
            "http://localhost:8080"
        );
    }

    #[test]
    fn lan_host_rewrites_loopback_rtc() {
        assert_eq!(
            advertised_rtc_url("http://localhost:7880", Some("10.19.144.83:8080")),
            "http://10.19.144.83:7880"
        );
        assert_eq!(
            advertised_rtc_url("http://localhost:7880", Some("127.0.0.1:8080")),
            "http://localhost:7880"
        );
        assert_eq!(
            advertised_rtc_url("http://livekit.example:7880", Some("10.19.144.83:8080")),
            "http://livekit.example:7880"
        );
    }
}
