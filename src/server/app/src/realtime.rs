use chat_protocol::GatewayEnvelope;
use std::{collections::HashMap, sync::Arc};
use tokio::sync::{Mutex, mpsc};
use uuid::Uuid;

const QUEUE: usize = 32;

#[derive(Clone)]
struct Client {
    session_id: Uuid,
    tx: mpsc::Sender<Arc<GatewayEnvelope>>,
}

#[derive(Clone)]
pub struct Hub {
    inner: Arc<Mutex<HashMap<Uuid, Vec<Client>>>>,
}

impl Hub {
    pub fn new() -> Self {
        Self {
            inner: Arc::new(Mutex::new(HashMap::new())),
        }
    }

    pub async fn subscribe(
        &self,
        user: Uuid,
        session_id: Uuid,
    ) -> mpsc::Receiver<Arc<GatewayEnvelope>> {
        let (tx, rx) = mpsc::channel(QUEUE);
        self.inner
            .lock()
            .await
            .entry(user)
            .or_default()
            .push(Client { session_id, tx });
        rx
    }

    pub async fn unsubscribe(&self, user: Uuid, session_id: Uuid) {
        let mut map = self.inner.lock().await;
        if let Some(clients) = map.get_mut(&user) {
            clients.retain(|c| c.session_id != session_id);
            if clients.is_empty() {
                map.remove(&user);
            }
        }
    }

    pub async fn is_connected(&self, user: Uuid) -> bool {
        self.inner
            .lock()
            .await
            .get(&user)
            .is_some_and(|clients| !clients.is_empty())
    }

    pub async fn send(&self, user: Uuid, envelope: GatewayEnvelope) {
        let payload = Arc::new(envelope);
        let mut dead = false;
        {
            let mut map = self.inner.lock().await;
            if let Some(clients) = map.get_mut(&user) {
                clients.retain(|client| client.tx.try_send(payload.clone()).is_ok());
                dead = clients.is_empty();
            }
            if dead {
                map.remove(&user);
            }
        }
    }
}
