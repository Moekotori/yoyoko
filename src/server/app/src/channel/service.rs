use super::repository::{ChannelRepository, RepositoryError};
use chat_domain::{
    channel::{Actor, Channel},
    permission::Permissions,
};
use std::future::Future;
use uuid::Uuid;

pub trait PermissionService: Send + Sync {
    fn require(
        &self,
        actor: Actor,
        channel: Uuid,
        permission: Permissions,
    ) -> impl Future<Output = Result<(), ServiceError>> + Send;
}
#[derive(Debug)]
pub enum ServiceError {
    Forbidden,
    NotFound,
    Unavailable,
}
impl From<RepositoryError> for ServiceError {
    fn from(_: RepositoryError) -> Self {
        Self::Unavailable
    }
}

pub struct ChannelService<R, P> {
    repository: R,
    permissions: P,
}
impl<R: ChannelRepository, P: PermissionService> ChannelService<R, P> {
    pub fn new(repository: R, permissions: P) -> Self {
        Self {
            repository,
            permissions,
        }
    }
    pub async fn get(&self, actor: Actor, id: Uuid) -> Result<Channel, ServiceError> {
        self.permissions
            .require(actor, id, Permissions::VIEW_CHANNEL)
            .await?;
        self.repository
            .find(id)
            .await?
            .ok_or(ServiceError::NotFound)
    }
}

// Not implemented yet: membership/role queries. Fail closed; never authorize a caller by default.
pub struct DenyAll;
impl PermissionService for DenyAll {
    async fn require(&self, _: Actor, _: Uuid, _: Permissions) -> Result<(), ServiceError> {
        Err(ServiceError::Forbidden)
    }
}
