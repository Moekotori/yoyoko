use chat_domain::voice::{AudioQuality, VoiceState};
use std::collections::HashMap;
use tokio::sync::Mutex;
use uuid::Uuid;

#[derive(Default)]
pub struct VoiceRoster {
    inner: Mutex<HashMap<Uuid, VoiceState>>,
}

impl VoiceRoster {
    pub async fn remove_channel(&self, channel_id: Uuid) -> Vec<VoiceState> {
        let mut inner = self.inner.lock().await;
        let mut removed = Vec::new();
        inner.retain(|_, state| {
            if state.channel_id == channel_id {
                removed.push(state.clone());
                false
            } else {
                true
            }
        });
        removed
    }

    pub async fn put(&self, state: VoiceState) -> Option<VoiceState> {
        self.inner.lock().await.insert(state.user_id, state)
    }

    pub async fn remove(&self, user_id: Uuid) -> Option<VoiceState> {
        self.inner.lock().await.remove(&user_id)
    }

    pub async fn get(&self, user_id: Uuid) -> Option<VoiceState> {
        self.inner.lock().await.get(&user_id).cloned()
    }

    pub async fn list_channel(&self, channel_id: Uuid) -> Vec<VoiceState> {
        self.inner
            .lock()
            .await
            .values()
            .filter(|state| state.channel_id == channel_id)
            .take(256)
            .cloned()
            .collect()
    }

    pub async fn for_servers(&self, server_ids: &[Uuid]) -> Vec<VoiceState> {
        self.inner
            .lock()
            .await
            .values()
            .filter(|state| server_ids.contains(&state.server_id))
            .take(256)
            .cloned()
            .collect()
    }

    pub async fn clamp_channel(&self, channel_id: Uuid, max: AudioQuality) -> Vec<VoiceState> {
        let mut inner = self.inner.lock().await;
        let mut changed = Vec::new();
        for state in inner.values_mut() {
            if state.channel_id != channel_id {
                continue;
            }
            let next = state.audio_quality.clamp(max);
            if next == state.audio_quality {
                continue;
            }
            state.audio_quality = next;
            changed.push(state.clone());
        }
        changed
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn moving_channels_replaces_membership() {
        let roster = VoiceRoster::default();
        let user = Uuid::now_v7();
        let server = Uuid::now_v7();
        let first = Uuid::now_v7();
        let second = Uuid::now_v7();
        roster
            .put(VoiceState {
                user_id: user,
                server_id: server,
                channel_id: first,
                self_mute: false,
                self_deaf: false,
                display_name: "Ada".into(),
                audio_quality: chat_domain::voice::AudioQuality::DEFAULT,
            })
            .await;
        roster
            .put(VoiceState {
                user_id: user,
                server_id: server,
                channel_id: second,
                self_mute: true,
                self_deaf: false,
                display_name: "Ada".into(),
                audio_quality: chat_domain::voice::AudioQuality::DEFAULT,
            })
            .await;
        assert!(roster.list_channel(first).await.is_empty());
        assert!(roster.list_channel(second).await[0].self_mute);
        let changed = roster.clamp_channel(second, AudioQuality::High).await;
        assert_eq!(changed.len(), 1);
        assert_eq!(changed[0].audio_quality, AudioQuality::High);
        assert!(
            roster
                .clamp_channel(second, AudioQuality::Studio)
                .await
                .is_empty()
        );
    }
}
