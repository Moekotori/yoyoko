#include "media.h"
#include <new>

// ABI/lifecycle only. No capture, device access, codec, or RTC implementation yet.
struct media_context { uint32_t abi = 1; };
static media_status unavailable(media_context *context) noexcept {
    return context ? MEDIA_NOT_IMPLEMENTED : MEDIA_INVALID_ARGUMENT;
}
uint32_t media_abi_version(void) noexcept { return 1; }
uint64_t media_capabilities(void) noexcept { return 0; }
media_status media_init(media_context **context) noexcept {
    if (!context) return MEDIA_INVALID_ARGUMENT;
    *context = new (std::nothrow) media_context{};
    return *context ? MEDIA_OK : MEDIA_INTERNAL_ERROR;
}
void media_shutdown(media_context *context) noexcept { delete context; }
media_status voice_join(media_context *context, const char *url, const char *token) noexcept {
    return url && token ? unavailable(context) : MEDIA_INVALID_ARGUMENT;
}
media_status voice_leave(media_context *context) noexcept { return unavailable(context); }
media_status set_input_device(media_context *context, const char *id) noexcept { return id ? unavailable(context) : MEDIA_INVALID_ARGUMENT; }
media_status set_output_device(media_context *context, const char *id) noexcept { return id ? unavailable(context) : MEDIA_INVALID_ARGUMENT; }
media_status set_noise_suppression(media_context *context, int32_t enabled) noexcept {
    return enabled == 0 || enabled == 1 ? unavailable(context) : MEDIA_INVALID_ARGUMENT;
}
media_status set_aec(media_context *context, int32_t enabled) noexcept {
    return enabled == 0 || enabled == 1 ? unavailable(context) : MEDIA_INVALID_ARGUMENT;
}
media_status screen_share_start(media_context *context, const char *source_id) noexcept { return source_id ? unavailable(context) : MEDIA_INVALID_ARGUMENT; }
media_status screen_share_stop(media_context *context) noexcept { return unavailable(context); }
