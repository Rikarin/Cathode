/*
 * cth_audio: the C ABI between Cathode's managed engine and the platform audio APIs.
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * The OS driver owns the callback thread. The shim deinterleaves device buffers into planar float
 * channels, enables flush-to-zero for the thread, and calls the registered process function.
 */
#ifndef CTH_AUDIO_H
#define CTH_AUDIO_H

#include <stdint.h>

#if defined(_WIN32)
#define CTH_API __declspec(dllexport)
#else
#define CTH_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Largest callback the shim will deliver; bigger device buffers are split. */
#define CTH_MAX_FRAMES 4096

typedef struct cth_stream cth_stream;

/* Planar callback. Output channels arrive zeroed. Runs on the real-time thread. */
typedef void (*cth_process_fn)(void *user, const float *const *inputs, int32_t input_channels,
                               float *const *outputs, int32_t output_channels, int32_t frames);

typedef struct cth_stream_config {
    double sample_rate;      /* in: requested rate, 0 keeps the device rate; out: actual */
    int32_t buffer_frames;   /* in: requested buffer size; out: actual */
    int32_t input_channels;  /* in: wanted input channels (0 = none); out: actual */
    int32_t output_channels; /* out: device output channels */
} cth_stream_config;

typedef struct cth_stream_stats {
    uint64_t callbacks;
    uint64_t overloads;            /* driver-reported overloads (dropouts) */
    int32_t input_latency_frames;  /* device + safety offset + stream latency + buffer */
    int32_t output_latency_frames;
} cth_stream_stats;

/* Wall-clock time of each callback measured around the whole process call, including any wait to enter the
 * managed runtime (for example while a GC is suspending managed threads). 1 % bins of the buffer period;
 * the last bin collects everything at or above 200 %. */
#define CTH_TIMING_BINS 201

typedef struct cth_stream_timing {
    uint64_t bins[CTH_TIMING_BINS];
    uint64_t over_budget; /* callbacks that took longer than their buffer period */
    double max_percent;
} cth_stream_timing;

typedef struct cth_device_info {
    char uid[256];
    char name[256];
    int32_t input_channels;
    int32_t output_channels;
    double sample_rate;
} cth_device_info;

/* 0 on success, negative on failure (see cth_audio_last_error). */
CTH_API int32_t cth_audio_device_count(void);
CTH_API int32_t cth_audio_device_info(int32_t index, cth_device_info *info);
/* The driver's name for one channel of a device (channel 0-based; input or output side), for labelling an input
 * map: a user label from Audio MIDI Setup or the driver's own ("Mic/Inst 1"). Writes an empty string when the
 * driver names none. 0 on success. */
CTH_API int32_t cth_audio_channel_name(int32_t device_index, int32_t is_input, int32_t channel, char *name,
                                       int32_t size);
CTH_API int32_t cth_audio_open(const char *device_uid, cth_stream_config *config,
                               cth_process_fn process, void *user, cth_stream **stream);
CTH_API int32_t cth_audio_start(cth_stream *stream);
CTH_API int32_t cth_audio_stop(cth_stream *stream);
CTH_API void cth_audio_close(cth_stream *stream);
CTH_API void cth_audio_stats(const cth_stream *stream, cth_stream_stats *stats);
CTH_API void cth_audio_timing(const cth_stream *stream, cth_stream_timing *timing);
CTH_API void cth_audio_reset_timing(cth_stream *stream);
CTH_API const char *cth_audio_backend(void);
CTH_API const char *cth_audio_last_error(void);

/* ---- MIDI (docs/02-architecture.md 5.10) ----------------------------------------------------------------------
 * cth_midi_open connects every MIDI input source; incoming messages are timestamped by the OS and queued in a
 * lock-free ring that cth_midi_read drains (from one thread). Times are nanoseconds on the cth_midi_now_ns clock.
 * Channel messages and system real-time (clock, start, stop) are delivered; SysEx is dropped. */

typedef struct cth_midi_port {
    char uid[64];   /* stable across runs (CoreMIDI unique id; WinMM "in:"/"out:" + name + "#n") */
    char name[256];
    int32_t is_input;
} cth_midi_port;

typedef struct cth_midi_event {
    uint64_t time_ns;
    uint8_t data[3];
    uint8_t length;
    uint8_t reserved[4];
} cth_midi_event;

CTH_API int32_t cth_midi_port_count(void);
CTH_API int32_t cth_midi_port_info(int32_t index, cth_midi_port *port);
/* 0 on success. Connects all current sources; call again to pick up devices plugged in since. */
CTH_API int32_t cth_midi_open(void);
/* Copies up to max queued events into events; returns how many. Events that overflowed the ring are lost. */
CTH_API int32_t cth_midi_read(cth_midi_event *events, int32_t max);
/* Sends a short message to the destination with this uid, or to every destination when uid is NULL. */
CTH_API int32_t cth_midi_send(const char *destination_uid, const uint8_t *data, int32_t length);
CTH_API void cth_midi_close(void);
CTH_API uint64_t cth_midi_now_ns(void);

/* Enables flush-to-zero / denormals-are-zero on the calling thread (x86 MXCSR, ARM64 FPCR.FZ). */
CTH_API void cth_fpu_flush_denormals(void);

#ifdef __cplusplus
}
#endif

#endif
