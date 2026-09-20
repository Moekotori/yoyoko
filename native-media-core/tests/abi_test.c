#include "media.h"
#define CHECK(condition) do { if (!(condition)) return __LINE__; } while (0)
int main(void) {
    media_context *context = 0;
    CHECK(media_abi_version() == 1);
    CHECK(media_capabilities() == 0);
    CHECK(media_init(0) == MEDIA_INVALID_ARGUMENT);
    CHECK(media_init(&context) == MEDIA_OK);
    CHECK(voice_join(context, "https://rtc.example.com", "test") == MEDIA_NOT_IMPLEMENTED);
    CHECK(screen_share_start(context, "display") == MEDIA_NOT_IMPLEMENTED);
    CHECK(voice_leave(0) == MEDIA_INVALID_ARGUMENT);
    media_shutdown(context);
    media_shutdown(0);
    return 0;
}
