# ADR 0006 — 语音走 LiveKit，控制面走聊天 Backend

Status: Accepted · 2026-09-20

## Context

媒体不能经过聊天 Backend；Native crash 不能拖死 UI。语音状态是高频临时数据。

## Decision

聊天 API 负责鉴权、CONNECT_VOICE/SPEAK、签发短期 LiveKit JWT、广播 `VOICE_STATE_UPDATE`。JWT `metadata` 携带发送音质档位（standard 64 kbps 单声道、high 128 kbps、very_high 384 kbps、studio 510 kbps Opus 上限）。频道存的是上限，用户在上限内自选；降低上限会钳制已在频道内的发送档位。Mute/deafen/改码率走 PATCH，不重签 token、不踢出房间。RTP 只到 LiveKit。桌面通过按需 `chat-media-worker` 进程接入 RTC，主进程只传控制命令。会话内复用 worker，不因 mute/音质杀掉进程。实时路径按 20 ms 帧、最多 3 帧抖动缓冲、满则丢旧帧；音频回调只 `try_lock`，不分配、不阻塞。输入/输出设备由 worker 枚举（短缓存），进房时按名称对到 LiveKit ADM。设置里的设备检查仍是本机回环试听。`chat-media-worker` 使用 LiveKit Rust SDK 发布麦克风并自动播放对端轨道；LiveKit 进程未运行时 join 返回连接错误，不伪装接通。

## Consequences

两个客户端可以先在同一语音频道里看见对方，再接真实听筒。自定义 RNNoise/C ABI 与进程恢复仍属后续阶段。
