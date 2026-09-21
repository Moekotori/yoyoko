# audio

Not implemented yet in this C ABI.

Voice transport is LiveKit in `chat-media-worker` via the LiveKit Rust SDK (PlatformAudio capture/playout, software AEC/NS/AGC). Control plane selects encoder profiles and devices. Local loopback remains a device check, not remote audio. Custom RNNoise / C ABI capture is not implemented yet.
