# audio

Not implemented yet in this C ABI.

Voice transport is LiveKit in `chat-media-worker`. Control plane already selects encoder profiles (48 kHz, 64/128/384/510 kbps). Planned here later: AEC / RNNoise / AGC / VAD and applying those profiles in the C ABI. Not implemented yet.
