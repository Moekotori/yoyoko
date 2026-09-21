use crate::{
    cache::HotCache, configuration::Settings, identity::TokenService, objects::ObjectStore,
    rate_limit::RateLimiter, realtime::Hub, store::Store, voice::VoiceRoster,
};
use std::sync::Arc;

pub struct AppState {
    pub settings: Settings,
    pub store: Arc<dyn Store>,
    pub objects: ObjectStore,
    pub tokens: TokenService,
    pub hub: Hub,
    pub limiter: RateLimiter,
    pub voice: VoiceRoster,
    pub hot: HotCache,
}

impl AppState {
    pub fn new(settings: Settings, store: Arc<dyn Store>, objects: ObjectStore) -> Arc<Self> {
        let tokens = TokenService::new(
            &settings.auth.token_secret,
            settings.auth.access_ttl_seconds,
            settings.auth.refresh_ttl_days,
        );
        Arc::new(Self {
            settings,
            store,
            objects,
            tokens,
            hub: Hub::new(),
            limiter: RateLimiter::new(),
            voice: VoiceRoster::default(),
            hot: HotCache::new(),
        })
    }
}
