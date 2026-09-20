use serde::Deserialize;
use std::net::SocketAddr;
use url::Url;
use uuid::Uuid;

#[derive(Clone, Deserialize)]
pub struct Settings {
    pub server: Server,
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
        let local = matches!(public.host_str(), Some("localhost" | "127.0.0.1" | "[::1]"));
        if !(public.scheme() == "https" || local && public.scheme() == "http")
            || public.path() != "/"
            || public.query().is_some()
            || public.fragment().is_some()
            || !public.username().is_empty()
            || public.password().is_some()
        {
            return Err(
                "server.public_url must be an HTTPS origin (HTTP allowed on loopback)".into(),
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
        for endpoint in [&self.storage.public_url, &self.rtc.public_url] {
            let uri = Url::parse(endpoint)?;
            if !(uri.scheme() == "https" || local && uri.scheme() == "http") {
                return Err(
                    "public storage/RTC URLs must use HTTPS outside local development".into(),
                );
            }
        }
        if self.storage.provider != "s3" || self.rtc.provider != "livekit" {
            return Err("unsupported storage/RTC provider".into());
        }
        Ok(())
    }
}
