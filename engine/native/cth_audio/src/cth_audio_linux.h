/* SPDX-License-Identifier: GPL-3.0-or-later
 * Shared between the Linux backends of cth_audio (ALSA PCM, JACK, ALSA sequencer MIDI). Not part of the ABI: every
 * symbol here keeps the library's default hidden visibility. */
#ifndef CTH_AUDIO_LINUX_H
#define CTH_AUDIO_LINUX_H

#include <stdatomic.h>
#include <stdint.h>

#include "cth_audio.h"

#define CTH_MAX_CHANNELS 64 /* channels delivered to the process function, per direction */

/* What every stream kind has in common: the callback, its planar pointers and the statistics. */
typedef struct cth_common {
    cth_process_fn process;
    void *user;
    int32_t input_channels;
    int32_t output_channels;
    int32_t buffer_frames;
    int32_t input_latency;
    int32_t output_latency;
    double sample_rate;
    _Atomic uint64_t callbacks;
    _Atomic uint64_t overloads;
    _Atomic int reset_timing; /* set by the control thread, applied by the audio thread */
    _Atomic int status;       /* CTH_STREAM_*, raised with cth_linux_raise_status */
    cth_stream_timing timing; /* written only by the audio thread */
    const float *in_ptrs[CTH_MAX_CHANNELS];
    float *out_ptrs[CTH_MAX_CHANNELS];
} cth_common;

/* RealtimeSanitizer (docs/03 §6). In a -DCTH_RTSAN=ON build the part of each cycle that must not allocate, lock or
 * block (conversion and the process call) runs in RTSan's real-time context, as the body of a [[clang::nonblocking]]
 * function would; RTSan then aborts on any allocation, lock or blocking call made there, the process callback's
 * included. The runtime lives in the checking executable (engine/native/rtsan), so the hooks are weak: loaded
 * anywhere else they do nothing. Other builds compile them out. */
#ifdef CTH_RTSAN
void __rtsan_realtime_enter(void) __attribute__((weak));
void __rtsan_realtime_exit(void) __attribute__((weak));
#define CTH_REALTIME_ENTER() (__rtsan_realtime_enter ? __rtsan_realtime_enter() : (void)0)
#define CTH_REALTIME_EXIT() (__rtsan_realtime_exit ? __rtsan_realtime_exit() : (void)0)
#else
#define CTH_REALTIME_ENTER() ((void)0)
#define CTH_REALTIME_EXIT() ((void)0)
#endif

/* CLOCK_MONOTONIC in nanoseconds (vDSO: no system call on the audio thread). */
uint64_t cth_linux_now_ns(void);

/* Applies a pending timing reset; call at the top of each cycle on the audio thread. */
void cth_linux_begin_cycle(cth_common *c);

/* Bins the wall-clock time of one cycle of frames that started at started_ns and counts the callback. */
void cth_linux_end_cycle(cth_common *c, uint64_t started_ns, int32_t frames);

/* Raises the stream status to at least value (CTH_STREAM_*; it never goes back down). Any thread. */
void cth_linux_raise_status(cth_common *c, int value);

/* Sets cth_audio_last_error for the calling thread and returns -1. */
int32_t cth_linux_fail(const char *format, ...) __attribute__((format(printf, 1, 2)));

/* Silences alsa-lib's stderr messages (keeping the last one for error reports). Idempotent. */
void cth_linux_alsa_init(void);

/* The last message alsa-lib reported on this thread, or an empty string. */
const char *cth_linux_alsa_message(void);

/* ---- JACK (cth_audio_jack.c), loaded with dlopen so a box without libjack still runs ALSA ---- */

typedef struct cth_jack_stream cth_jack_stream;

/* Lists the JACK server's devices (one per client that owns physical ports, "system" under jackd) when a server is
 * running. Fills up to max entries; returns how many. */
int32_t cth_jack_devices(cth_device_info *devices, int32_t max);

/* client is the part of the uid after "jack:". The common block must outlive the stream. */
int32_t cth_jack_open(const char *client, cth_stream_config *config, cth_common *common, cth_jack_stream **stream);
int32_t cth_jack_start(cth_jack_stream *stream);
int32_t cth_jack_stop(cth_jack_stream *stream);
void cth_jack_close(cth_jack_stream *stream);

#endif
