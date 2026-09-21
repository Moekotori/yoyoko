use crate::{
    error::{ApiErr, ApiResult},
    services::{dispatch, to_protocol_channel},
    state::AppState,
};
use uuid::Uuid;

pub async fn list(state: &AppState, user: Uuid) -> ApiResult<Vec<chat_protocol::Channel>> {
    Ok(state
        .store
        .list_dms(user)
        .await?
        .into_iter()
        .map(to_protocol_channel)
        .collect())
}

pub async fn open(
    state: &AppState,
    user: Uuid,
    recipient: Uuid,
) -> ApiResult<chat_protocol::Channel> {
    if user == recipient {
        return Err(ApiErr::bad(
            "invalid_recipient",
            "Cannot message yourself.",
        ));
    }
    state
        .store
        .find_user(recipient)
        .await?
        .ok_or_else(ApiErr::not_found)?;
    if state.store.find_dm(user, recipient).await?.is_none()
        && !state.store.shares_community(user, recipient).await?
    {
        return Err(ApiErr::forbidden());
    }
    let (channel, created) = state.store.open_dm(user, recipient).await?;
    state.hot.put_channel(channel.clone()).await;
    if created {
        let members = if channel.participants.len() >= 2 {
            channel.participants.clone()
        } else {
            vec![user, recipient]
        };
        let payload = serde_json::to_value(to_protocol_channel(channel.clone())).unwrap_or_default();
        dispatch(state, &members, "CHANNEL_CREATE", payload).await?;
    }
    Ok(to_protocol_channel(channel))
}
