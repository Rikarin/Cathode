/* SPDX-License-Identifier: GPL-3.0-or-later
 * The parts of the ASIO backend (cth_audio_asio.cpp) that need neither Windows nor a driver: sample conversion,
 * buffer-size negotiation and parsing the driver list in HKLM\SOFTWARE\ASIO. Kept apart so the native unit tests
 * (tests/cth_asio_util_tests.c) build and run on every platform. Not part of the ABI. */
#ifndef CTH_ASIO_UTIL_H
#define CTH_ASIO_UTIL_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ASIOSampleType values (asio.h) the shim converts. Big-endian and DSD types are not handled. */
#define CTH_ASIO_INT16_LSB 16
#define CTH_ASIO_INT24_LSB 17 /* packed, three bytes per sample */
#define CTH_ASIO_INT32_LSB 18
#define CTH_ASIO_FLOAT32_LSB 19
#define CTH_ASIO_FLOAT64_LSB 20
#define CTH_ASIO_INT32_LSB16 24 /* 32-bit containers holding 16, 18, 20 or 24-bit samples in their low bits */
#define CTH_ASIO_INT32_LSB18 25
#define CTH_ASIO_INT32_LSB20 26
#define CTH_ASIO_INT32_LSB24 27

/* Bytes per sample of an ASIO sample type, or 0 when the shim does not handle it. */
int32_t cth_asio_sample_bytes(int32_t type);

/* A readable name for an ASIO sample type ("Int24LSB"), for error messages. */
const char *cth_asio_sample_name(int32_t type);

/* Converts frames samples of one channel, starting offset frames into the driver's buffer, to float in [-1, 1). */
void cth_asio_to_float(int32_t type, const void *buffer, int32_t offset, float *out, int32_t frames);

/* Converts frames float samples into one channel of the driver's buffer at offset frames, clipping to [-1, 1]. */
void cth_asio_from_float(int32_t type, const float *in, void *buffer, int32_t offset, int32_t frames);

/* The buffer size to ask the driver for, from its ASIOGetBufferSize answer: wanted rounded up to the driver's
 * granularity (-1 means powers of two) within [min, max], or preferred when wanted is 0 or the driver offers one
 * size only. */
int32_t cth_asio_pick_buffer(int32_t min, int32_t max, int32_t preferred, int32_t granularity, int32_t wanted);

/* A COM class id, laid out as a Windows GUID. */
typedef struct cth_asio_guid {
    uint32_t data1;
    uint16_t data2;
    uint16_t data3;
    uint8_t data4[8];
} cth_asio_guid;

/* Parses "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}" (braces optional, any case). 0 on success. */
int cth_asio_parse_clsid(const char *text, cth_asio_guid *out);

/* One installed driver. */
typedef struct cth_asio_driver {
    char key[128];  /* the registry key name under SOFTWARE\ASIO; stable, so it makes the uid */
    char uid[256];  /* "asio:" + key */
    char name[256]; /* the key's Description value, else the key name; " (ASIO)" is appended unless it says so, to
                       tell it from the same interface's WASAPI endpoint in a device list */
    cth_asio_guid clsid;
} cth_asio_driver;

/* Builds a driver entry from one registry key (UTF-8 strings; description may be NULL or empty). 0 on success,
 * -1 when the key is unusable (no name, or no parsable CLSID): such keys are skipped. */
int cth_asio_driver_entry(const char *key, const char *clsid, const char *description, cth_asio_driver *out);

/* The driver key in an "asio:" uid, or NULL when uid is not an ASIO uid. */
const char *cth_asio_uid_key(const char *uid);

#ifdef __cplusplus
}
#endif

#endif
