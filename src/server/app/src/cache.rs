use crate::store::PermissionSnapshot;
use chat_domain::channel::{Channel, Server};
use std::{
    collections::HashMap,
    hash::Hash,
    time::{Duration, Instant},
};
use tokio::sync::Mutex;
use uuid::Uuid;

struct Entry<V> {
    value: V,
    at: Instant,
}

struct TtlMap<K, V> {
    inner: Mutex<HashMap<K, Entry<V>>>,
    ttl: Duration,
    cap: usize,
}

impl<K: Eq + Hash + Clone, V: Clone> TtlMap<K, V> {
    fn new(cap: usize, ttl: Duration) -> Self {
        Self {
            inner: Mutex::new(HashMap::new()),
            ttl,
            cap,
        }
    }

    async fn get(&self, key: &K) -> Option<V> {
        let mut map = self.inner.lock().await;
        let now = Instant::now();
        match map.get(key) {
            Some(entry) if now.duration_since(entry.at) < self.ttl => Some(entry.value.clone()),
            Some(_) => {
                map.remove(key);
                None
            }
            None => None,
        }
    }

    async fn insert(&self, key: K, value: V) {
        let mut map = self.inner.lock().await;
        let now = Instant::now();
        if map.len() >= self.cap {
            map.retain(|_, entry| now.duration_since(entry.at) < self.ttl);
        }
        if map.len() >= self.cap {
            let mut oldest: Vec<(Instant, K)> = map
                .iter()
                .map(|(key, entry)| (entry.at, key.clone()))
                .collect();
            oldest.sort_by_key(|(at, _)| *at);
            for (_, key) in oldest.into_iter().take((self.cap / 8).max(1)) {
                map.remove(&key);
            }
        }
        map.insert(
            key,
            Entry {
                value,
                at: Instant::now(),
            },
        );
    }

    async fn remove(&self, key: &K) {
        self.inner.lock().await.remove(key);
    }

    async fn retain(&self, keep: impl Fn(&K) -> bool) {
        self.inner.lock().await.retain(|key, _| keep(key));
    }
}

/// Short-lived, bounded process cache for hot reads. Not a second source of truth.
pub struct HotCache {
    channels: TtlMap<Uuid, Channel>,
    servers: TtlMap<Uuid, Server>,
    access: TtlMap<(Uuid, Uuid), PermissionSnapshot>,
    names: TtlMap<Uuid, Vec<(Uuid, String, String)>>,
}

impl HotCache {
    pub fn new() -> Self {
        Self {
            channels: TtlMap::new(512, Duration::from_secs(30)),
            servers: TtlMap::new(256, Duration::from_secs(15)),
            access: TtlMap::new(2_048, Duration::from_secs(8)),
            names: TtlMap::new(128, Duration::from_secs(15)),
        }
    }

    pub async fn channel(&self, id: Uuid) -> Option<Channel> {
        self.channels.get(&id).await
    }

    pub async fn put_channel(&self, channel: Channel) {
        self.channels.insert(channel.id, channel).await;
    }

    pub async fn server(&self, id: Uuid) -> Option<Server> {
        self.servers.get(&id).await
    }

    pub async fn put_server(&self, server: Server) {
        self.servers.insert(server.id, server).await;
    }

    pub async fn access(&self, user: Uuid, channel: Uuid) -> Option<PermissionSnapshot> {
        self.access.get(&(user, channel)).await
    }

    pub async fn put_access(&self, user: Uuid, channel: Uuid, snapshot: PermissionSnapshot) {
        self.access.insert((user, channel), snapshot).await;
    }

    pub async fn names(&self, server: Uuid) -> Option<Vec<(Uuid, String, String)>> {
        self.names.get(&server).await
    }

    pub async fn put_names(&self, server: Uuid, names: Vec<(Uuid, String, String)>) {
        self.names.insert(server, names).await;
    }

    pub async fn forget_channel(&self, id: Uuid) {
        self.channels.remove(&id).await;
        self.access.retain(|(_, channel)| *channel != id).await;
    }

    pub async fn forget_server(&self, id: Uuid) {
        self.servers.remove(&id).await;
        self.names.remove(&id).await;
        let drop_ids: Vec<Uuid> = {
            let channels = self.channels.inner.lock().await;
            channels
                .iter()
                .filter(|(_, entry)| entry.value.server_id == Some(id))
                .map(|(key, _)| *key)
                .collect()
        };
        for channel in drop_ids {
            self.forget_channel(channel).await;
        }
    }

    pub async fn forget_user(&self, user: Uuid) {
        self.access.retain(|(cached, _)| *cached != user).await;
        self.names.inner.lock().await.clear();
    }
}

impl Default for HotCache {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::store::PermissionSnapshot;
    use chat_domain::permission::Permissions;

    #[tokio::test]
    async fn expired_and_over_cap_entries_are_dropped() {
        let map: TtlMap<u8, u8> = TtlMap::new(2, Duration::from_millis(20));
        map.insert(1, 10).await;
        map.insert(2, 20).await;
        map.insert(3, 30).await;
        assert!(map.inner.lock().await.len() <= 2);
        tokio::time::sleep(Duration::from_millis(30)).await;
        assert!(map.get(&3).await.is_none());
    }

    #[tokio::test]
    async fn forget_channel_drops_matching_access() {
        let hot = HotCache::new();
        let channel = Uuid::now_v7();
        let user = Uuid::now_v7();
        hot.put_access(
            user,
            channel,
            PermissionSnapshot {
                member: true,
                base: Permissions::VIEW_CHANNEL,
                roles: vec![],
                everyone: (Permissions(0), Permissions(0)),
                role_overrides: vec![],
                member_override: (Permissions(0), Permissions(0)),
            },
        )
        .await;
        hot.forget_channel(channel).await;
        assert!(hot.access(user, channel).await.is_none());
    }
}
