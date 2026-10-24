/* SPDX-License-Identifier: GPL-3.0-or-later
 * MIDI placeholder for platforms without a backend yet (none of macOS, Windows and Linux uses it). */
#include <stddef.h>
#include <time.h>
#include "cth_audio.h"

int32_t cth_midi_port_count(void) { return 0; }
int32_t cth_midi_port_info(int32_t index, cth_midi_port *port) { (void)index; (void)port; return -1; }
int32_t cth_midi_open(void) { return -1; }
int32_t cth_midi_read(cth_midi_event *events, int32_t max) { (void)events; (void)max; return 0; }
int32_t cth_midi_send(const char *uid, const uint8_t *data, int32_t length) { (void)uid; (void)data; (void)length; return -1; }
void cth_midi_close(void) { }

uint64_t cth_midi_now_ns(void)
{
    struct timespec now;
    timespec_get(&now, TIME_UTC);
    return (uint64_t)now.tv_sec * 1000000000u + (uint64_t)now.tv_nsec;
}
