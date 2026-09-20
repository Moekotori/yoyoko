# audio

Not implemented yet in this C ABI.

Voice transport is LiveKit in `chat-media-worker`. Control plane already selects encoder profiles (48 kHz, 64/128/384/510 kbps) and input/output devices (enumerated in the worker). Planned here later: AEC / RNNoise / AGC / VAD and applying those profiles and device IDs in the C ABI. Not implemented yet.
