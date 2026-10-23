/*
 * cth_nam: Neural Amp Modeler (.nam) models behind a C ABI, for Cathode's managed engine.
 * Wraps NeuralAmpModelerCore (MIT). SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Threading: everything except cth_nam_process runs on the control thread and may allocate.
 * cth_nam_process is real-time safe once cth_nam_reset has been called with a large enough max_frames.
 */
#ifndef CTH_NAM_H
#define CTH_NAM_H

#include <stdint.h>

#if defined(_WIN32)
#define CTH_NAM_API __declspec(dllexport)
#else
#define CTH_NAM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct cth_nam_model cth_nam_model;

typedef struct cth_nam_info {
    char architecture[64];       /* "WaveNet", "LSTM", "SlimmableContainer", ... */
    char version[32];            /* .nam file format version */
    double expected_sample_rate; /* training rate in Hz, or -1 if unknown */
    int32_t has_loudness;
    double loudness;             /* dB, from the model metadata */
    int32_t has_input_level;
    double input_level;          /* dBu for 0 dBFS at the capture input */
    int32_t has_output_level;
    double output_level;
    int32_t prewarm_samples;
    int32_t slimmable;           /* 1 when cth_nam_set_size is supported */
} cth_nam_info;

/* Loads a .nam model (or a .wav impulse response as a linear model). 0 on success. */
CTH_NAM_API int32_t cth_nam_load(const char *path, cth_nam_model **model);
CTH_NAM_API void cth_nam_get_info(const cth_nam_model *model, cth_nam_info *info);

/* Allocates for blocks of up to max_frames and pre-warms the model so its first output is settled. */
CTH_NAM_API int32_t cth_nam_reset(cth_nam_model *model, double sample_rate, int32_t max_frames);

/* Slimmable models only: 0.0 = smallest, 1.0 = full size. Not real-time safe; call before publishing. */
CTH_NAM_API int32_t cth_nam_set_size(cth_nam_model *model, double size);

/* Real-time thread. Mono in, mono out; input and output may alias. Longer blocks are split internally. */
CTH_NAM_API void cth_nam_process(cth_nam_model *model, const float *input, float *output, int32_t frames);

CTH_NAM_API void cth_nam_free(cth_nam_model *model);

/* Process-global: affects models loaded afterwards (and LSTM processing immediately). */
CTH_NAM_API void cth_nam_set_fast_tanh(int32_t enabled);

CTH_NAM_API const char *cth_nam_core_version(void);
CTH_NAM_API const char *cth_nam_last_error(void);

#ifdef __cplusplus
}
#endif

#endif
