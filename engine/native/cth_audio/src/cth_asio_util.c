/* SPDX-License-Identifier: GPL-3.0-or-later
 * See cth_asio_util.h. Runs on the driver's callback thread (the conversions) and the control thread (the rest):
 * nothing here allocates or locks. */
#include "cth_asio_util.h"

#include <stdio.h>
#include <string.h>

int32_t cth_asio_sample_bytes(int32_t type)
{
    switch (type) {
    case CTH_ASIO_INT16_LSB: return 2;
    case CTH_ASIO_INT24_LSB: return 3;
    case CTH_ASIO_INT32_LSB:
    case CTH_ASIO_FLOAT32_LSB:
    case CTH_ASIO_INT32_LSB16:
    case CTH_ASIO_INT32_LSB18:
    case CTH_ASIO_INT32_LSB20:
    case CTH_ASIO_INT32_LSB24: return 4;
    case CTH_ASIO_FLOAT64_LSB: return 8;
    default: return 0;
    }
}

const char *cth_asio_sample_name(int32_t type)
{
    static const char *const names[] = {
        "Int16MSB", "Int24MSB", "Int32MSB", "Float32MSB", "Float64MSB", "?", "?", "?",
        "Int32MSB16", "Int32MSB18", "Int32MSB20", "Int32MSB24", "?", "?", "?", "?",
        "Int16LSB", "Int24LSB", "Int32LSB", "Float32LSB", "Float64LSB", "?", "?", "?",
        "Int32LSB16", "Int32LSB18", "Int32LSB20", "Int32LSB24", "?", "?", "?", "?",
        "DSDInt8LSB1", "DSDInt8MSB1", "?", "?", "?", "?", "?", "?", "DSDInt8NER8",
    };
    if (type < 0 || type >= (int32_t)(sizeof names / sizeof names[0]) || names[type][0] == '?') return "unknown";
    return names[type];
}

/* Significant bits of the integer types held in 32-bit containers (Int32LSB itself uses all 32). */
static int container_bits(int32_t type)
{
    switch (type) {
    case CTH_ASIO_INT32_LSB16: return 16;
    case CTH_ASIO_INT32_LSB18: return 18;
    case CTH_ASIO_INT32_LSB20: return 20;
    case CTH_ASIO_INT32_LSB24: return 24;
    default: return 32;
    }
}

void cth_asio_to_float(int32_t type, const void *buffer, int32_t offset, float *out, int32_t frames)
{
    const uint8_t *p = (const uint8_t *)buffer + (size_t)offset * (size_t)cth_asio_sample_bytes(type);
    switch (type) {
    case CTH_ASIO_FLOAT32_LSB:
        memcpy(out, p, (size_t)frames * sizeof(float));
        break;
    case CTH_ASIO_FLOAT64_LSB:
        for (int32_t i = 0; i < frames; i++) {
            double v;
            memcpy(&v, p + (size_t)i * 8, 8);
            out[i] = (float)v;
        }
        break;
    case CTH_ASIO_INT16_LSB:
        for (int32_t i = 0; i < frames; i++) {
            int16_t v;
            memcpy(&v, p + (size_t)i * 2, 2);
            out[i] = (float)v * (1.0f / 32768.0f);
        }
        break;
    case CTH_ASIO_INT24_LSB:
        for (int32_t i = 0; i < frames; i++) {
            const uint8_t *s = p + (size_t)i * 3;
            int32_t v = (int32_t)(((uint32_t)s[0] << 8) | ((uint32_t)s[1] << 16) | ((uint32_t)s[2] << 24)) >> 8;
            out[i] = (float)v * (1.0f / 8388608.0f);
        }
        break;
    case CTH_ASIO_INT32_LSB:
    case CTH_ASIO_INT32_LSB16:
    case CTH_ASIO_INT32_LSB18:
    case CTH_ASIO_INT32_LSB20:
    case CTH_ASIO_INT32_LSB24: {
        /* The low bits carry the sample, sign-extended through the container. */
        float scale = 1.0f / (float)((uint64_t)1 << (container_bits(type) - 1));
        for (int32_t i = 0; i < frames; i++) {
            int32_t v;
            memcpy(&v, p + (size_t)i * 4, 4);
            out[i] = (float)v * scale;
        }
        break;
    }
    default:
        memset(out, 0, (size_t)frames * sizeof(float));
        break;
    }
}

static float clip(float v) { return v > 1.0f ? 1.0f : v < -1.0f ? -1.0f : v; }

/* A clipped sample scaled to a signed integer of bits bits (full scale +1.0 maps to the largest value). */
static int32_t to_int(float v, int bits)
{
    v = clip(v);
    if (bits == 32) return v >= 1.0f ? INT32_MAX : (int32_t)((double)v * 2147483648.0);
    int32_t full = (int32_t)(1u << (bits - 1));
    return v >= 1.0f ? full - 1 : (int32_t)(v * (float)full);
}

void cth_asio_from_float(int32_t type, const float *in, void *buffer, int32_t offset, int32_t frames)
{
    uint8_t *p = (uint8_t *)buffer + (size_t)offset * (size_t)cth_asio_sample_bytes(type);
    switch (type) {
    case CTH_ASIO_FLOAT32_LSB:
        memcpy(p, in, (size_t)frames * sizeof(float));
        break;
    case CTH_ASIO_FLOAT64_LSB:
        for (int32_t i = 0; i < frames; i++) {
            double v = in[i];
            memcpy(p + (size_t)i * 8, &v, 8);
        }
        break;
    case CTH_ASIO_INT16_LSB:
        for (int32_t i = 0; i < frames; i++) {
            int16_t v = (int16_t)to_int(in[i], 16);
            memcpy(p + (size_t)i * 2, &v, 2);
        }
        break;
    case CTH_ASIO_INT24_LSB:
        for (int32_t i = 0; i < frames; i++) {
            int32_t v = to_int(in[i], 24);
            uint8_t *d = p + (size_t)i * 3;
            d[0] = (uint8_t)v;
            d[1] = (uint8_t)(v >> 8);
            d[2] = (uint8_t)(v >> 16);
        }
        break;
    case CTH_ASIO_INT32_LSB:
    case CTH_ASIO_INT32_LSB16:
    case CTH_ASIO_INT32_LSB18:
    case CTH_ASIO_INT32_LSB20:
    case CTH_ASIO_INT32_LSB24: {
        int bits = container_bits(type);
        for (int32_t i = 0; i < frames; i++) {
            int32_t v = to_int(in[i], bits);
            memcpy(p + (size_t)i * 4, &v, 4);
        }
        break;
    }
    default:
        break;
    }
}

int32_t cth_asio_pick_buffer(int32_t min, int32_t max, int32_t preferred, int32_t granularity, int32_t wanted)
{
    if (min <= 0 || max < min) return preferred > 0 ? preferred : min;
    if (preferred < min || preferred > max) preferred = min;
    if (wanted <= 0 || min == max || granularity == 0) return preferred;

    int64_t size;
    if (granularity < 0) {
        /* Powers of two from min up. */
        size = min;
        while (size < wanted && size * 2 <= max) size *= 2;
    } else {
        int64_t steps = wanted <= min ? 0 : ((int64_t)wanted - min + granularity - 1) / granularity;
        size = min + steps * granularity;
        if (size > max) size = max - ((int64_t)max - min) % granularity;
    }
    return (int32_t)size;
}

static int hex_digit(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

/* Reads digits hex digits from *text into *value, advancing *text. */
static int read_hex(const char **text, int digits, uint32_t *value)
{
    *value = 0;
    for (int i = 0; i < digits; i++) {
        int d = hex_digit((*text)[0]);
        if (d < 0) return -1;
        *value = (*value << 4) | (uint32_t)d;
        (*text)++;
    }
    return 0;
}

static int expect(const char **text, char c)
{
    if ((*text)[0] != c) return -1;
    (*text)++;
    return 0;
}

int cth_asio_parse_clsid(const char *text, cth_asio_guid *out)
{
    if (!text || !out) return -1;
    while (*text == ' ') text++;
    int braced = text[0] == '{';
    if (braced) text++;
    uint32_t v;
    if (read_hex(&text, 8, &v) < 0) return -1;
    out->data1 = v;
    if (expect(&text, '-') < 0 || read_hex(&text, 4, &v) < 0) return -1;
    out->data2 = (uint16_t)v;
    if (expect(&text, '-') < 0 || read_hex(&text, 4, &v) < 0) return -1;
    out->data3 = (uint16_t)v;
    if (expect(&text, '-') < 0) return -1;
    for (int i = 0; i < 8; i++) {
        if (i == 2 && expect(&text, '-') < 0) return -1;
        if (read_hex(&text, 2, &v) < 0) return -1;
        out->data4[i] = (uint8_t)v;
    }
    if (braced && expect(&text, '}') < 0) return -1;
    while (*text == ' ') text++;
    return *text == 0 ? 0 : -1;
}

/* Whether text contains "asio" in any case. */
static int mentions_asio(const char *text)
{
    for (; text[0]; text++) {
        int match = 1;
        for (int i = 0; i < 4 && match; i++) match = text[i] && (text[i] | 0x20) == "asio"[i];
        if (match) return 1;
    }
    return 0;
}

int cth_asio_driver_entry(const char *key, const char *clsid, const char *description, cth_asio_driver *out)
{
    if (!key || !key[0] || !out) return -1;
    memset(out, 0, sizeof *out);
    if (strlen(key) >= sizeof out->key || cth_asio_parse_clsid(clsid, &out->clsid) < 0) return -1;
    snprintf(out->key, sizeof out->key, "%s", key);
    snprintf(out->uid, sizeof out->uid, "asio:%s", key);
    const char *name = description && description[0] ? description : key;
    snprintf(out->name, sizeof out->name, mentions_asio(name) ? "%s" : "%s (ASIO)", name);
    return 0;
}

const char *cth_asio_uid_key(const char *uid)
{
    return uid && strncmp(uid, "asio:", 5) == 0 ? uid + 5 : NULL;
}
