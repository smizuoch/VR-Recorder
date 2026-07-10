#include <stdint.h>

#include "vrrecorder_native.h"

#if UINTPTR_MAX == UINT64_MAX
_Static_assert(sizeof(vrrec_session_config_v1) == 40, "config ABI drift");
_Static_assert(sizeof(vrrec_event_v1) == 48, "event ABI drift");
_Static_assert(sizeof(vrrec_callbacks_v1) == 24, "callback ABI drift");
#endif

static void VRREC_CALL consume_event(
    void *user_data,
    const vrrec_event_v1 *event)
{
    (void)user_data;
    (void)event;
}

int vrrec_c_header_smoke(void)
{
    vrrec_callbacks_v1 callbacks = {
        sizeof(vrrec_callbacks_v1),
        VRREC_ABI_V1,
        consume_event,
        0,
    };
    return callbacks.on_event == 0;
}
