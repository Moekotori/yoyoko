#ifndef CHAT_MEDIA_H
#define CHAT_MEDIA_H
#include <stdint.h>
#if defined(_WIN32)
  #if defined(MEDIA_BUILD)
    #define MEDIA_API __declspec(dllexport)
  #else
    #define MEDIA_API __declspec(dllimport)
  #endif
#else
  #define MEDIA_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#define MEDIA_NOEXCEPT noexcept
#else
#define MEDIA_NOEXCEPT
#endif

typedef struct media_context media_context;
typedef int32_t media_status;
enum { MEDIA_OK = 0, MEDIA_NOT_IMPLEMENTED = 1, MEDIA_INVALID_ARGUMENT = 2, MEDIA_INTERNAL_ERROR = 3 };
/* Borrowed UTF-8 inputs, opaque owned handle; no C++ types or exceptions across ABI. */
MEDIA_API uint32_t media_abi_version(void) MEDIA_NOEXCEPT;
MEDIA_API uint64_t media_capabilities(void) MEDIA_NOEXCEPT;
MEDIA_API media_status media_init(media_context **context) MEDIA_NOEXCEPT;
MEDIA_API void media_shutdown(media_context *context) MEDIA_NOEXCEPT;
MEDIA_API media_status voice_join(media_context *context, const char *url, const char *token) MEDIA_NOEXCEPT;
MEDIA_API media_status voice_leave(media_context *context) MEDIA_NOEXCEPT;
MEDIA_API media_status set_input_device(media_context *context, const char *id) MEDIA_NOEXCEPT;
MEDIA_API media_status set_output_device(media_context *context, const char *id) MEDIA_NOEXCEPT;
MEDIA_API media_status set_noise_suppression(media_context *context, int32_t enabled) MEDIA_NOEXCEPT;
MEDIA_API media_status set_aec(media_context *context, int32_t enabled) MEDIA_NOEXCEPT;
MEDIA_API media_status screen_share_start(media_context *context, const char *source_id) MEDIA_NOEXCEPT;
MEDIA_API media_status screen_share_stop(media_context *context) MEDIA_NOEXCEPT;
#ifdef __cplusplus
}
#endif
#endif
