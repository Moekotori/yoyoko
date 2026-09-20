use chat_domain::channel::Channel;
use std::future::Future;
use uuid::Uuid;

#[derive(Debug)]
pub enum RepositoryError {
    Unavailable,
}

// Service owns the port; PostgreSQL is an adapter. No Axum/SQL row types escape.
pub trait ChannelRepository: Send + Sync {
    fn find(
        &self,
        id: Uuid,
    ) -> impl Future<Output = Result<Option<Channel>, RepositoryError>> + Send;
}
