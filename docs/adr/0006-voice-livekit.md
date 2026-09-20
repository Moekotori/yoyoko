# ADR 0006 — 语音走 LiveKit，控制面走聊天 Backend

Status: Accepted · 2026-09-20

## Context

媒体不能经过聊天 Backend；Native crash 不能拖死 UI。语音状态是高频临时数据。

## Decision

聊天 API 负责鉴权、CONNECT_VOICE/SPEAK、签发短期 LiveKit JWT、广播 `VOICE_STATE_UPDATE`。JWT `metadata` 携带发送音质档位（standard 64 kbps 单声道、high 128 kbps、very_high 384 kbps、studio 510 kbps Opus 上限）。频道存的是上限，用户在上限内自选；降低上限会钳制已在频道内的发送档位。RTP 只到 LiveKit。桌面通过按需 `chat-media-worker` 进程接入 RTC，主进程只传控制命令。Worker 未链接或 LiveKit 不可达时明确失败，频道成员列表仍以 Gateway 为准。编码器按这些档位工作仍属后续，不能把控制面选择写成已经出声。

## Consequences

两个客户端可以先在同一语音频道里看见对方，再接真实听筒。自定义 RNNoise/C ABI 与进程恢复仍属后续阶段。
