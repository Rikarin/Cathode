/* SPDX-License-Identifier: GPL-3.0-or-later
 * Placeholder for platforms whose backend is not written yet (JACK on Linux). */
#include <stddef.h>
#include "cth_audio.h"

static const char *const not_supported = "no audio backend for this platform yet";

int32_t cth_audio_device_count(void) { return 0; }
int32_t cth_audio_device_info(int32_t index, cth_device_info *info) { (void)index; (void)info; return -1; }

int32_t cth_audio_channel_name(int32_t device_index, int32_t is_input, int32_t channel, char *name, int32_t size)
{
    (void)device_index; (void)is_input; (void)channel;
    if (name && size > 0) name[0] = 0;
    return -1;
}

int32_t cth_audio_open(const char *device_uid, cth_stream_config *config, cth_process_fn process, void *user, cth_stream **stream)
{
    (void)device_uid; (void)config; (void)process; (void)user;
    if (stream) *stream = NULL;
    return -1;
}

int32_t cth_audio_start(cth_stream *stream) { (void)stream; return -1; }
int32_t cth_audio_stop(cth_stream *stream) { (void)stream; return -1; }
void cth_audio_close(cth_stream *stream) { (void)stream; }

void cth_audio_stats(const cth_stream *stream, cth_stream_stats *stats)
{
    (void)stream;
    if (stats) { stats->callbacks = 0; stats->overloads = 0; stats->input_latency_frames = 0; stats->output_latency_frames = 0; }
}

void cth_audio_timing(const cth_stream *stream, cth_stream_timing *timing)
{
    (void)stream;
    if (timing) { for (int i = 0; i < CTH_TIMING_BINS; i++) timing->bins[i] = 0; timing->over_budget = 0; timing->max_percent = 0; }
}

void cth_audio_reset_timing(cth_stream *stream) { (void)stream; }

const char *cth_audio_backend(void) { return "none"; }
const char *cth_audio_last_error(void) { return not_supported; }
