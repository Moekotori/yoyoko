# audio

Not implemented yet in this C ABI.

Voice transport is LiveKit in `chat-media-worker`. Control plane already selects encoder profiles (48 kHz, 20 ms frames, 3-frame jitter, 64/128/384/510 kbps) and input/output devices. The worker can loop those frames locally for a device check. Planned here later: AEC / RNNoise / AGC / VAD, Opus on the C ABI, and RTP to LiveKit. Not implemented yet.
