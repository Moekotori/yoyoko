# Native media core

C++20 with a versioned C ABI. Build and lifecycle are implemented; all voice/capture/device operations return `MEDIA_NOT_IMPLEMENTED`. Capabilities = 0.

`media_init` allocates one opaque handle; exactly one owner must call `media_shutdown`. Inputs are borrowed only for the duration of a call. Invalid/double-freed handles are caller bugs. C++ exceptions must never cross the ABI. `noexcept` is not crash isolation: segmentation faults terminate the hosting process.

The desktop deliberately does **not** load this library. Real RTC will be hosted in an on-demand worker before enabling media in the client. Only control messages and low-rate status cross IPC; PCM, GPU textures and video frames stay native. Worker crashes become a disconnected media state. Phase 7 hardens scheduling, restart and deployment of that worker.

```sh
cmake -S native-media-core -B build/native
cmake --build build/native --config Release
ctest --test-dir build/native -C Release --output-on-failure
```
