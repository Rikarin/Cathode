/* SPDX-License-Identifier: GPL-3.0-or-later
 * Shared between the Windows backends of cth_audio: WASAPI (cth_audio_wasapi.c), which owns the public functions,
 * and ASIO (cth_audio_asio.cpp), which they hand "asio:" device uids to. Not part of the ABI. */
#ifndef CTH_AUDIO_WIN_H
#define CTH_AUDIO_WIN_H

#include <stdint.h>

#include "cth_audio.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Sets cth_audio_last_error for the calling thread (cth_audio_wasapi.c). */
void cth_win_set_error(const char *message);

/* ---- ASIO (cth_audio_asio.cpp) ----
 * One ASIO driver can be loaded per process: while a stream is open, opening another fails, and enumeration reports
 * the other drivers as last seen instead of loading them. */

typedef struct cth_asio_stream cth_asio_stream;

/* Re-reads the installed drivers (HKLM\SOFTWARE\ASIO) and loads each in turn for its channels and rate. Returns how
 * many there are; cth_asio_device_info and cth_asio_channel_name read the list this built. */
int32_t cth_asio_device_count(void);
int32_t cth_asio_device_info(int32_t index, cth_device_info *info);
int32_t cth_asio_channel_name(int32_t index, int32_t is_input, int32_t channel, char *name, int32_t size);

/* uid is the whole "asio:<driver key>" device uid. */
int32_t cth_asio_open(const char *uid, cth_stream_config *config, cth_process_fn process, void *user,
                      cth_asio_stream **stream);
int32_t cth_asio_start(cth_asio_stream *stream);
int32_t cth_asio_stop(cth_asio_stream *stream);
void cth_asio_close(cth_asio_stream *stream);
void cth_asio_stats(cth_asio_stream *stream, cth_stream_stats *stats);
void cth_asio_timing(const cth_asio_stream *stream, cth_stream_timing *timing);
void cth_asio_reset_timing(cth_asio_stream *stream);
int32_t cth_asio_status(const cth_asio_stream *stream);

#ifdef __cplusplus
}
#endif

#endif
