use crate::{
    error::{ApiErr, ApiResult},
    services::{
        audience, dispatch, parse_quality, publish_voice, require, to_protocol_channel,
        validate_display,
    },
    state::AppState,
};
use chat_domain::{channel::ChannelKind, permission::Permissions};
use uuid::Uuid;

pub async fn update(
    state: &AppState,
    user: Uuid,
    id: Uuid,
    name: Option<String>,
    audio_quality: Option<String>,
) -> ApiResult<chat_protocol::Channel> {
    let channel = require(state, user, id, Permissions::MANAGE_CHANNEL).await?;
    let name = name.map(|value| value.trim().to_owned());
    if let Some(value) = &name {
        validate_display(value)?;
    }
    let quality = if let Some(value) = audio_quality.filter(|value| !value.trim().is_empty()) {
        if channel.kind != ChannelKind::Voice {
            return Err(ApiErr::bad(
                "invalid_channel",
                "audio_quality only applies to voice channels.",
            ));
        }
        Some(parse_quality(Some(&value))?)
    } else {
        None
    };
    let updated = state.store.update_channel(id, name, quality).await?;
    state.hot.forget_channel(id).await;
    state.hot.put_channel(updated.clone()).await;
    if let Some(quality) = quality {
        for voice in state.voice.clamp_channel(id, quality).await {
            publish_voice(state, &voice, false).await?;
        }
    }
    let members = audience(state, &updated).await?;
    let channel = to_protocol_channel(updated);
    dispatch(
        state,
        &members,
        "CHANNEL_UPDATE",
        serde_json::to_value(&channel).unwrap_or_default(),
    )
    .await?;
    Ok(channel)
}

pub async fn delete(state: &AppState, user: Uuid, id: Uuid) -> ApiResult<()> {
    let channel = require(state, user, id, Permissions::MANAGE_CHANNEL).await?;
    let members = audience(state, &channel).await?;
    state.store.delete_channel(id).await?;
    state.hot.forget_channel(id).await;
    for voice in state.voice.remove_channel(id).await {
        publish_voice(state, &voice, true).await?;
    }
    dispatch(
        state,
        &members,
        "CHANNEL_DELETE",
        serde_json::to_value(to_protocol_channel(channel)).unwrap_or_default(),
    )
    .await
}
