/* SPDX-License-Identifier: GPL-3.0-or-later
 * Unit tests of the ASIO backend's driver-independent parts (src/cth_asio_util.c): sample conversion, buffer-size
 * negotiation and driver-list parsing. No driver or Windows needed; run by ctest after native/build.sh. */
#include <math.h>
#include <stdio.h>
#include <string.h>

#include "cth_asio_util.h"

static int failures;

#define CHECK(condition)                                                                    \
    do {                                                                                    \
        if (!(condition)) {                                                                 \
            fprintf(stderr, "%s:%d: CHECK failed: %s\n", __FILE__, __LINE__, #condition);   \
            failures++;                                                                     \
        }                                                                                   \
    } while (0)

#define CHECK_NEAR(actual, expected, tolerance) CHECK(fabs((double)(actual) - (double)(expected)) <= (tolerance))

static const int32_t integer_types[] = {
    CTH_ASIO_INT16_LSB,   CTH_ASIO_INT24_LSB,   CTH_ASIO_INT32_LSB,   CTH_ASIO_INT32_LSB16,
    CTH_ASIO_INT32_LSB18, CTH_ASIO_INT32_LSB20, CTH_ASIO_INT32_LSB24,
};

static double step_of(int32_t type)
{
    switch (type) {
    case CTH_ASIO_INT16_LSB:
    case CTH_ASIO_INT32_LSB16: return 1.0 / 32768.0;
    case CTH_ASIO_INT32_LSB18: return 1.0 / 131072.0;
    case CTH_ASIO_INT32_LSB20: return 1.0 / 524288.0;
    case CTH_ASIO_INT24_LSB:
    case CTH_ASIO_INT32_LSB24: return 1.0 / 8388608.0;
    default: return 1.0 / 2147483648.0;
    }
}

static void sample_sizes(void)
{
    CHECK(cth_asio_sample_bytes(CTH_ASIO_INT16_LSB) == 2);
    CHECK(cth_asio_sample_bytes(CTH_ASIO_INT24_LSB) == 3);
    CHECK(cth_asio_sample_bytes(CTH_ASIO_INT32_LSB) == 4);
    CHECK(cth_asio_sample_bytes(CTH_ASIO_FLOAT32_LSB) == 4);
    CHECK(cth_asio_sample_bytes(CTH_ASIO_FLOAT64_LSB) == 8);
    CHECK(cth_asio_sample_bytes(CTH_ASIO_INT32_LSB24) == 4);
    CHECK(cth_asio_sample_bytes(0) == 0);  /* Int16MSB */
    CHECK(cth_asio_sample_bytes(32) == 0); /* DSD */
    CHECK(strcmp(cth_asio_sample_name(CTH_ASIO_INT24_LSB), "Int24LSB") == 0);
    CHECK(strcmp(cth_asio_sample_name(2), "Int32MSB") == 0);
    CHECK(strcmp(cth_asio_sample_name(99), "unknown") == 0);
}

/* Known bit patterns decode to the expected floats. */
static void decoding(void)
{
    const uint8_t i24[] = {0x00, 0x00, 0x80, 0xff, 0xff, 0x7f, 0x00, 0x00, 0x40, 0xff, 0xff, 0xff};
    float out[4];
    cth_asio_to_float(CTH_ASIO_INT24_LSB, i24, 0, out, 4);
    CHECK(out[0] == -1.0f);
    CHECK_NEAR(out[1], 8388607.0 / 8388608.0, 1e-7);
    CHECK(out[2] == 0.5f);
    CHECK_NEAR(out[3], -1.0 / 8388608.0, 1e-9);

    const int16_t i16[] = {-32768, 16384, -1, 32767};
    cth_asio_to_float(CTH_ASIO_INT16_LSB, i16, 0, out, 4);
    CHECK(out[0] == -1.0f);
    CHECK(out[1] == 0.5f);
    CHECK_NEAR(out[2], -1.0 / 32768.0, 1e-9);

    const int32_t i32[] = {INT32_MIN, 0x40000000, 0, -0x40000000};
    cth_asio_to_float(CTH_ASIO_INT32_LSB, i32, 0, out, 4);
    CHECK(out[0] == -1.0f);
    CHECK(out[1] == 0.5f);
    CHECK(out[2] == 0.0f);
    CHECK(out[3] == -0.5f);

    /* Int32LSB24: a 24-bit sample right-aligned and sign-extended in 32 bits. */
    const int32_t lsb24[] = {-8388608, 4194304, -4194304, 0};
    cth_asio_to_float(CTH_ASIO_INT32_LSB24, lsb24, 0, out, 4);
    CHECK(out[0] == -1.0f);
    CHECK(out[1] == 0.5f);
    CHECK(out[2] == -0.5f);

    const double f64[] = {0.25, -0.75};
    cth_asio_to_float(CTH_ASIO_FLOAT64_LSB, f64, 0, out, 2);
    CHECK(out[0] == 0.25f);
    CHECK(out[1] == -0.75f);

    /* The offset is in frames of the type: start at the second Int24 sample. */
    cth_asio_to_float(CTH_ASIO_INT24_LSB, i24, 2, out, 2);
    CHECK(out[0] == 0.5f);
}

/* Encoding clips, maps +1.0 to the largest code, and lands on the right bytes. */
static void encoding(void)
{
    const float in[] = {1.0f, -1.0f, 0.5f, 2.0f, -3.0f, 0.0f};
    uint8_t i24[18];
    cth_asio_from_float(CTH_ASIO_INT24_LSB, in, i24, 0, 6);
    CHECK(i24[0] == 0xff && i24[1] == 0xff && i24[2] == 0x7f);   /* +1.0 -> 8388607 */
    CHECK(i24[3] == 0x00 && i24[4] == 0x00 && i24[5] == 0x80);   /* -1.0 -> -8388608 */
    CHECK(i24[6] == 0x00 && i24[7] == 0x00 && i24[8] == 0x40);   /* 0.5 -> 4194304 */
    CHECK(i24[9] == 0xff && i24[10] == 0xff && i24[11] == 0x7f); /* clipped high */
    CHECK(i24[12] == 0x00 && i24[13] == 0x00 && i24[14] == 0x80); /* clipped low */

    int16_t i16[6];
    cth_asio_from_float(CTH_ASIO_INT16_LSB, in, i16, 0, 6);
    CHECK(i16[0] == 32767 && i16[1] == -32768 && i16[2] == 16384 && i16[3] == 32767 && i16[4] == -32768);

    int32_t i32[6];
    cth_asio_from_float(CTH_ASIO_INT32_LSB, in, i32, 0, 6);
    CHECK(i32[0] == INT32_MAX && i32[1] == INT32_MIN && i32[2] == 0x40000000 && i32[5] == 0);

    int32_t lsb20[6];
    cth_asio_from_float(CTH_ASIO_INT32_LSB20, in, lsb20, 0, 6);
    CHECK(lsb20[0] == 524287 && lsb20[1] == -524288 && lsb20[2] == 262144);

    float f32[6];
    cth_asio_from_float(CTH_ASIO_FLOAT32_LSB, in, f32, 0, 6);
    CHECK(memcmp(f32, in, sizeof in) == 0); /* float passes through unclipped, as the WASAPI path does */

    /* Writing at an offset leaves the samples before it alone. */
    int16_t partial[4] = {7, 7, 7, 7};
    cth_asio_from_float(CTH_ASIO_INT16_LSB, in + 2, partial, 2, 2);
    CHECK(partial[0] == 7 && partial[1] == 7 && partial[2] == 16384 && partial[3] == 32767);
}

/* Every integer type survives float -> driver -> float within half a code step, across the whole range. */
static void round_trip(void)
{
    float in[201], back[201];
    uint8_t raw[201 * 8];
    for (int i = 0; i < 201; i++) in[i] = (float)(i - 100) / 100.0f;
    for (size_t t = 0; t < sizeof integer_types / sizeof integer_types[0]; t++) {
        int32_t type = integer_types[t];
        cth_asio_from_float(type, in, raw, 0, 201);
        cth_asio_to_float(type, raw, 0, back, 201);
        for (int i = 0; i < 201; i++) CHECK_NEAR(back[i], in[i], step_of(type) + 1e-7);
    }
    cth_asio_from_float(CTH_ASIO_FLOAT64_LSB, in, raw, 0, 201);
    cth_asio_to_float(CTH_ASIO_FLOAT64_LSB, raw, 0, back, 201);
    CHECK(memcmp(in, back, sizeof in) == 0);
}

static void buffer_sizes(void)
{
    /* Powers of two (granularity -1). */
    CHECK(cth_asio_pick_buffer(32, 2048, 256, -1, 64) == 64);
    CHECK(cth_asio_pick_buffer(32, 2048, 256, -1, 100) == 128);
    CHECK(cth_asio_pick_buffer(32, 2048, 256, -1, 16) == 32);
    CHECK(cth_asio_pick_buffer(32, 2048, 256, -1, 9000) == 2048);
    /* Linear steps. */
    CHECK(cth_asio_pick_buffer(48, 1024, 192, 16, 100) == 112);
    CHECK(cth_asio_pick_buffer(48, 1024, 192, 16, 10) == 48);
    CHECK(cth_asio_pick_buffer(48, 1000, 192, 16, 5000) == 992);
    CHECK(cth_asio_pick_buffer(64, 2048, 128, 1, 96) == 96);
    /* No request, a fixed size, or granularity 0: the preferred size. */
    CHECK(cth_asio_pick_buffer(32, 2048, 256, -1, 0) == 256);
    CHECK(cth_asio_pick_buffer(512, 512, 512, 0, 64) == 512);
    CHECK(cth_asio_pick_buffer(64, 1024, 256, 0, 128) == 256);
    /* A preferred size outside the range falls back to the minimum. */
    CHECK(cth_asio_pick_buffer(64, 1024, 4096, -1, 0) == 64);
}

static void clsids(void)
{
    cth_asio_guid g;
    CHECK(cth_asio_parse_clsid("{232685C6-6548-49D8-846D-4141A3EF7560}", &g) == 0);
    CHECK(g.data1 == 0x232685C6u && g.data2 == 0x6548 && g.data3 == 0x49D8);
    CHECK(g.data4[0] == 0x84 && g.data4[1] == 0x6D && g.data4[2] == 0x41 && g.data4[7] == 0x60);
    CHECK(cth_asio_parse_clsid("232685c6-6548-49d8-846d-4141a3ef7560", &g) == 0);
    CHECK(cth_asio_parse_clsid(" {232685C6-6548-49D8-846D-4141A3EF7560} ", &g) == 0);
    CHECK(cth_asio_parse_clsid("{232685C6-6548-49D8-846D-4141A3EF7560", &g) < 0);
    CHECK(cth_asio_parse_clsid("{232685C6-6548-49D8-846D4141A3EF7560}", &g) < 0);
    CHECK(cth_asio_parse_clsid("{232685C6-6548-49D8-846D-4141A3EF756}", &g) < 0);
    CHECK(cth_asio_parse_clsid("{232685C6-6548-49D8-846D-4141A3EF7560}x", &g) < 0);
    CHECK(cth_asio_parse_clsid("", &g) < 0);
    CHECK(cth_asio_parse_clsid(NULL, &g) < 0);
}

static void driver_entries(void)
{
    cth_asio_driver d;
    CHECK(cth_asio_driver_entry("Focusrite USB ASIO", "{232685C6-6548-49D8-846D-4141A3EF7560}",
                                "Focusrite USB 2.0 Audio Driver", &d) == 0);
    CHECK(strcmp(d.uid, "asio:Focusrite USB ASIO") == 0);
    CHECK(strcmp(d.key, "Focusrite USB ASIO") == 0);
    CHECK(strcmp(d.name, "Focusrite USB 2.0 Audio Driver (ASIO)") == 0); /* told from the WASAPI endpoint */
    CHECK(d.clsid.data1 == 0x232685C6u);

    /* No Description: the key names the device. */
    CHECK(cth_asio_driver_entry("ASIO4ALL v2", "{232685C6-6548-49D8-846D-4141A3EF7560}", NULL, &d) == 0);
    CHECK(strcmp(d.name, "ASIO4ALL v2") == 0);
    CHECK(cth_asio_driver_entry("ASIO4ALL v2", "{232685C6-6548-49D8-846D-4141A3EF7560}", "", &d) == 0);
    CHECK(strcmp(d.name, "ASIO4ALL v2") == 0);

    CHECK(cth_asio_driver_entry("Yamaha Steinberg USB", "{232685C6-6548-49D8-846D-4141A3EF7560}",
                                "Yamaha Steinberg USB Asio", &d) == 0);
    CHECK(strcmp(d.name, "Yamaha Steinberg USB Asio") == 0);
    CHECK(cth_asio_driver_entry("MOTU Pro Audio", "{232685C6-6548-49D8-846D-4141A3EF7560}", NULL, &d) == 0);
    CHECK(strcmp(d.name, "MOTU Pro Audio (ASIO)") == 0);

    /* Keys without a usable CLSID or name are skipped. */
    CHECK(cth_asio_driver_entry("Broken", NULL, "Broken driver", &d) < 0);
    CHECK(cth_asio_driver_entry("Broken", "not a guid", NULL, &d) < 0);
    CHECK(cth_asio_driver_entry("", "{232685C6-6548-49D8-846D-4141A3EF7560}", NULL, &d) < 0);

    CHECK(strcmp(cth_asio_uid_key("asio:Focusrite USB ASIO"), "Focusrite USB ASIO") == 0);
    CHECK(cth_asio_uid_key("jack:system") == NULL);
    CHECK(cth_asio_uid_key("{0.0.0.00000000}.{guid}") == NULL);
    CHECK(cth_asio_uid_key(NULL) == NULL);
}

int main(void)
{
    sample_sizes();
    decoding();
    encoding();
    round_trip();
    buffer_sizes();
    clsids();
    driver_entries();
    if (failures) {
        fprintf(stderr, "%d check(s) failed\n", failures);
        return 1;
    }
    printf("cth_asio_util: all checks passed\n");
    return 0;
}
