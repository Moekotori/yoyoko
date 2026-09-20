use std::{
    collections::HashMap,
    time::{Duration, Instant},
};
use tokio::sync::Mutex;

pub struct RateLimiter {
    inner: Mutex<HashMap<String, Vec<Instant>>>,
}

impl Default for RateLimiter {
    fn default() -> Self {
        Self {
            inner: Mutex::new(HashMap::new()),
        }
    }
}

impl RateLimiter {
    pub fn new() -> Self {
        Self::default()
    }

    pub async fn check(&self, key: &str, max: usize, window: Duration) -> bool {
        let now = Instant::now();
        let mut map = self.inner.lock().await;
        if map.len() > 10_000 {
            map.retain(|_, hits| hits.last().is_some_and(|t| now.duration_since(*t) < window));
        }
        let hits = map.entry(key.to_string()).or_default();
        hits.retain(|t| now.duration_since(*t) < window);
        if hits.len() >= max {
            return false;
        }
        hits.push(now);
        true
    }
}
