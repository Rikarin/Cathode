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
CTH_API int32_t cth_audio_open(const char *device_uid, cth_stream_config *config,
                               cth_process_fn process, void *user, cth_stream **stream);
CTH_API int32_t cth_audio_start(cth_stream *stream);
CTH_API int32_t cth_audio_stop(cth_stream *stream);
CTH_API void cth_audio_close(cth_stream *stream);
CTH_API void cth_audio_stats(const cth_stream *stream, cth_stream_stats *stats);
CTH_API const char *cth_audio_backend(void);
CTH_API const char *cth_audio_last_error(void);

/* Enables flush-to-zero / denormals-are-zero on the calling thread (x86 MXCSR, ARM64 FPCR.FZ). */
CTH_API void cth_fpu_flush_denormals(void);

#ifdef __cplusplus
}
#endif

#endif
