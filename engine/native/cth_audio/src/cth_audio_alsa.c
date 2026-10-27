/*
 * ALSA backend: direct hw: PCM access on Linux, so a headless box needs no sound server. The shim owns the real-time
 * thread (SCHED_FIFO when the process may use it), runs capture and playback full duplex on one card with the two
 * streams linked, and converts the device's integer samples to and from planar float. Device ids prefixed "jack:" go
 * to the JACK backend instead (cth_audio_jack.c).
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Device ids: "alsa:<card id>" is PCM device 0 of a card ("alsa:USB" is hw:CARD=USB,DEV=0) and "alsa:<card id>,<n>"
 * another device on it; card ids survive reboots and replugging, unlike card numbers. Any other id, with or without
 * the "alsa:" prefix ("hw:1,0", "plughw:USB", "null"), is passed to snd_pcm_open as a PCM name.
 *
 * The cycle: wait for one period of capture, process it, write one period of playback. Playback is primed with a full
 * buffer of silence when the streams start, so a sample captured in one period plays (periods - 1) periods after
 * that period ends: the round trip is periods x period frames plus the converters, which ALSA does not report.
 */
#define _GNU_SOURCE
#include <alsa/asoundlib.h>
#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <pthread.h>
#include <sched.h>
#include <stdarg.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>

#include "cth_audio.h"
#include "cth_audio_linux.h"

#if __BYTE_ORDER__ != __ORDER_LITTLE_ENDIAN__
#error "the ALSA backend reads and writes little-endian sample formats in host order"
#endif

#define MAX_DEVICE_CHANNELS 256 /* opened on the hardware; at most CTH_MAX_CHANNELS reach the callback */
#define MAX_POLL_FDS 16
#define MAX_DEVICES 64
#define DEFAULT_PRIORITY 70
#define STOPPED 1 /* a wait ended because cth_audio_stop asked */

typedef enum sample_kind { S32, S24_3, S24, S16, F32 } sample_kind;

static const struct {
    snd_pcm_format_t format;
    sample_kind kind;
    int bytes;
} formats[] = {
    {SND_PCM_FORMAT_S32_LE, S32, 4},  {SND_PCM_FORMAT_S24_3LE, S24_3, 3}, {SND_PCM_FORMAT_S24_LE, S24, 4},
    {SND_PCM_FORMAT_S16_LE, S16, 2},  {SND_PCM_FORMAT_FLOAT_LE, F32, 4},
};

/* How one direction of the stream is laid out on the device. */
typedef struct pcm_layout {
    snd_pcm_t *pcm;
    sample_kind kind;
    int bytes;          /* per sample */
    int noninterleaved; /* one buffer per channel (readn/writen) instead of interleaved frames */
    int32_t channels;   /* opened on the device */
    uint8_t *raw;       /* one period in the device format */
    void *areas[MAX_DEVICE_CHANNELS]; /* noninterleaved transfers: each channel's position */
    struct pollfd fds[MAX_POLL_FDS + 1]; /* the PCM's descriptors, then the wake pipe */
    int nfds;
} pcm_layout;

struct cth_stream {
    cth_common common; /* first: the JACK backend sees only this */
    cth_jack_stream *jack;
    pcm_layout out;
    pcm_layout in; /* in.pcm is NULL without inputs */
    int linked;
    snd_pcm_uframes_t period;
    unsigned periods;
    int timeout_ms;
    uint8_t *silence; /* one period of zeros in the playback format, one channel's worth per channel */
    float *planar;    /* (inputs + outputs) * CTH_MAX_FRAMES */
    int wake[2];
    pthread_t thread;
    int thread_started;
    _Atomic int running;
};

typedef struct device_entry {
    cth_device_info info;
    char pcm[128];
} device_entry;

static _Thread_local char last_error[256];
static _Thread_local char alsa_message[256];
static pthread_mutex_t devices_lock = PTHREAD_MUTEX_INITIALIZER;
static device_entry devices[MAX_DEVICES];
static int32_t device_total;
static _Atomic int backend_is_jack;

/* ---- shared with the JACK and MIDI files -------------------------------------------------------------------- */

uint64_t cth_linux_now_ns(void)
{
    struct timespec now;
    clock_gettime(CLOCK_MONOTONIC, &now);
    return (uint64_t)now.tv_sec * 1000000000u + (uint64_t)now.tv_nsec;
}

void cth_linux_raise_status(cth_common *c, int value)
{
    int old = atomic_load_explicit(&c->status, memory_order_relaxed);
    while (old < value && !atomic_compare_exchange_weak(&c->status, &old, value)) {
    }
}

void cth_linux_begin_cycle(cth_common *c)
{
    if (atomic_exchange_explicit(&c->reset_timing, 0, memory_order_acquire)) memset(&c->timing, 0, sizeof c->timing);
}

void cth_linux_end_cycle(cth_common *c, uint64_t started_ns, int32_t frames)
{
    if (frames > 0 && c->sample_rate > 0) {
        double elapsed_ns = (double)(cth_linux_now_ns() - started_ns);
        double percent = elapsed_ns / (frames / c->sample_rate * 1e9) * 100.0;
        int bin = percent >= CTH_TIMING_BINS - 1 ? CTH_TIMING_BINS - 1 : (int)percent;
        c->timing.bins[bin]++;
        if (percent > c->timing.max_percent) c->timing.max_percent = percent;
        if (percent >= 100.0) c->timing.over_budget++;
    }

    atomic_fetch_add_explicit(&c->callbacks, 1, memory_order_relaxed);
}

int32_t cth_linux_fail(const char *format, ...)
{
    va_list args;
    va_start(args, format);
    vsnprintf(last_error, sizeof last_error, format, args);
    va_end(args);
    return -1;
}

static void quiet_alsa(const char *file, int line, const char *function, int err, const char *format, ...)
{
    (void)file; (void)line; (void)function; (void)err;
    va_list args;
    va_start(args, format);
    vsnprintf(alsa_message, sizeof alsa_message, format, args);
    va_end(args);
}

static void install_alsa_handler(void) { snd_lib_error_set_handler(quiet_alsa); }

void cth_linux_alsa_init(void)
{
    static pthread_once_t once = PTHREAD_ONCE_INIT;
    pthread_once(&once, install_alsa_handler);
}

const char *cth_linux_alsa_message(void) { return alsa_message; }

const char *cth_audio_last_error(void) { return last_error; }
const char *cth_audio_backend(void) { return atomic_load(&backend_is_jack) ? "jack" : "alsa"; }

static int32_t fail_alsa(const char *what, const char *pcm, int err)
{
    return cth_linux_fail("%s %s (%s)", what, pcm, snd_strerror(err));
}

/* ---- device enumeration ------------------------------------------------------------------------------------- */

/* Opens one direction of a PCM just long enough to read its channel count and pick a rate (48 kHz when allowed). */
static int probe(const char *pcm_name, snd_pcm_stream_t direction, int32_t *channels, double *rate)
{
    snd_pcm_t *pcm = NULL;
    int err = snd_pcm_open(&pcm, pcm_name, direction, SND_PCM_NONBLOCK);
    if (err < 0) return err;
    snd_pcm_hw_params_t *hw;
    snd_pcm_hw_params_alloca(&hw);
    unsigned max = 0;
    err = snd_pcm_hw_params_any(pcm, hw);
    if (err >= 0) {
        snd_pcm_hw_params_get_channels_max(hw, &max);
        *channels = (int32_t)(max > MAX_DEVICE_CHANNELS ? MAX_DEVICE_CHANNELS : max);
        unsigned min_rate = 0;
        int dir = 0;
        if (snd_pcm_hw_params_test_rate(pcm, hw, 48000, 0) == 0) *rate = 48000;
        else if (snd_pcm_hw_params_test_rate(pcm, hw, 44100, 0) == 0) *rate = 44100;
        else if (snd_pcm_hw_params_get_rate_min(hw, &min_rate, &dir) == 0) *rate = min_rate;
    }

    snd_pcm_close(pcm);
    return err < 0 ? err : 0;
}

/* Rebuilds the device table: every PCM device of every card, then the JACK server's devices. A device that is busy
 * (open elsewhere, including by our own running stream) keeps the channel counts it had in the previous table. */
static void refresh_devices(void)
{
    static device_entry previous[MAX_DEVICES];
    int32_t previous_total = device_total;
    memcpy(previous, devices, sizeof previous);
    device_total = 0;
    cth_linux_alsa_init();

    int card = -1;
    while (snd_card_next(&card) == 0 && card >= 0 && device_total < MAX_DEVICES) {
        char ctl_name[32];
        snprintf(ctl_name, sizeof ctl_name, "hw:%d", card);
        snd_ctl_t *ctl = NULL;
        if (snd_ctl_open(&ctl, ctl_name, 0) < 0) continue;
        snd_ctl_card_info_t *card_info;
        snd_ctl_card_info_alloca(&card_info);
        if (snd_ctl_card_info(ctl, card_info) < 0) {
            snd_ctl_close(ctl);
            continue;
        }

        const char *id = snd_ctl_card_info_get_id(card_info);
        const char *card_name = snd_ctl_card_info_get_name(card_info);
        int pcm_devices[32], pcm_count = 0;
        for (int dev = -1; snd_ctl_pcm_next_device(ctl, &dev) == 0 && dev >= 0 && pcm_count < 32;) {
            pcm_devices[pcm_count++] = dev;
        }

        for (int i = 0; i < pcm_count && device_total < MAX_DEVICES; i++) {
            int dev = pcm_devices[i];
            device_entry *e = &devices[device_total];
            memset(e, 0, sizeof *e);
            snprintf(e->pcm, sizeof e->pcm, "hw:CARD=%s,DEV=%d", id, dev);
            if (dev == 0) snprintf(e->info.uid, sizeof e->info.uid, "alsa:%s", id);
            else snprintf(e->info.uid, sizeof e->info.uid, "alsa:%s,%d", id, dev);

            snd_pcm_info_t *pcm_info;
            snd_pcm_info_alloca(&pcm_info);
            snd_pcm_info_set_device(pcm_info, (unsigned)dev);
            snd_pcm_info_set_subdevice(pcm_info, 0);
            snd_pcm_info_set_stream(pcm_info, SND_PCM_STREAM_PLAYBACK);
            int has_playback = snd_ctl_pcm_info(ctl, pcm_info) == 0;
            snd_pcm_info_set_stream(pcm_info, SND_PCM_STREAM_CAPTURE);
            int has_capture = snd_ctl_pcm_info(ctl, pcm_info) == 0;
            if (pcm_count > 1 && (has_playback || has_capture)) {
                snprintf(e->info.name, sizeof e->info.name, "%s: %s", card_name, snd_pcm_info_get_name(pcm_info));
            } else {
                snprintf(e->info.name, sizeof e->info.name, "%s", card_name);
            }

            int busy = 0;
            if (has_playback) busy |= probe(e->pcm, SND_PCM_STREAM_PLAYBACK, &e->info.output_channels, &e->info.sample_rate) < 0;
            if (has_capture) busy |= probe(e->pcm, SND_PCM_STREAM_CAPTURE, &e->info.input_channels, &e->info.sample_rate) < 0;
            if (busy) {
                for (int32_t p = 0; p < previous_total; p++) {
                    if (strcmp(previous[p].info.uid, e->info.uid) == 0) {
                        e->info = previous[p].info;
                        break;
                    }
                }
            }

            device_total++;
        }

        snd_ctl_close(ctl);
    }

    cth_device_info jack[8];
    int32_t jack_count = cth_jack_devices(jack, 8);
    for (int32_t i = 0; i < jack_count && device_total < MAX_DEVICES; i++) {
        memset(&devices[device_total], 0, sizeof devices[device_total]);
        devices[device_total++].info = jack[i];
    }
}

int32_t cth_audio_device_count(void)
{
    pthread_mutex_lock(&devices_lock);
    refresh_devices();
    int32_t count = device_total;
    pthread_mutex_unlock(&devices_lock);
    return count;
}

/* The table built by the last cth_audio_device_count (the managed side counts, then asks for each device). */
static int32_t device_at(int32_t index, device_entry *out)
{
    pthread_mutex_lock(&devices_lock);
    if (device_total == 0) refresh_devices();
    int32_t ok = index >= 0 && index < device_total;
    if (ok) *out = devices[index];
    pthread_mutex_unlock(&devices_lock);
    return ok ? 0 : cth_linux_fail("device index out of range");
}

int32_t cth_audio_device_info(int32_t index, cth_device_info *info)
{
    device_entry e;
    if (!info) return cth_linux_fail("invalid arguments");
    if (device_at(index, &e) < 0) return -1;
    *info = e.info;
    return 0;
}

/* ALSA has no channel names (USB descriptors carry none that the driver exposes), so every channel is unnamed. */
int32_t cth_audio_channel_name(int32_t device_index, int32_t is_input, int32_t channel, char *name, int32_t size)
{
    if (!name || size <= 0) return cth_linux_fail("invalid arguments");
    name[0] = 0;
    device_entry e;
    if (device_at(device_index, &e) < 0) return -1;
    int32_t channels = is_input ? e.info.input_channels : e.info.output_channels;
    if (channel < 0 || channel >= channels) return cth_linux_fail("channel out of range");
    return 0;
}

/* The device to open for a NULL or empty id: the first duplex device, else the first with outputs. */
static int default_device(char *uid, size_t size)
{
    pthread_mutex_lock(&devices_lock);
    refresh_devices();
    int32_t found = -1;
    for (int32_t i = 0; i < device_total && found < 0; i++) {
        if (devices[i].info.input_channels > 0 && devices[i].info.output_channels > 0) found = i;
    }
    for (int32_t i = 0; i < device_total && found < 0; i++) {
        if (devices[i].info.output_channels > 0) found = i;
    }
    if (found >= 0) snprintf(uid, size, "%s", devices[found].info.uid);
    pthread_mutex_unlock(&devices_lock);
    return found >= 0 ? 0 : cth_linux_fail("no audio device with outputs found");
}

/* Maps a device id to a PCM name (ALSA) or a JACK client name; see the top of the file. */
static int resolve(const char *uid, char *target, size_t size, int *jack)
{
    char fallback[256];
    *jack = 0;
    if (!uid || !uid[0]) {
        if (default_device(fallback, sizeof fallback) < 0) return -1;
        uid = fallback;
    }

    if (strncmp(uid, "jack:", 5) == 0) {
        *jack = 1;
        snprintf(target, size, "%s", uid + 5);
        return 0;
    }

    if (strncmp(uid, "alsa:", 5) != 0) {
        snprintf(target, size, "%s", uid);
        return 0;
    }

    const char *rest = uid + 5;
    const char *comma = strchr(rest, ',');
    if (strchr(rest, ':')) snprintf(target, size, "%s", rest);
    else if (comma) snprintf(target, size, "hw:CARD=%.*s,DEV=%s", (int)(comma - rest), rest, comma + 1);
    else snprintf(target, size, "hw:CARD=%s,DEV=0", rest);
    return 0;
}

/* ---- sample conversion ------------------------------------------------------------------------------------- */

static void to_float(const uint8_t *src, size_t stride, float *dst, snd_pcm_uframes_t frames, sample_kind kind)
{
    switch (kind) {
    case S32:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, src += stride) {
            int32_t v;
            memcpy(&v, src, 4);
            dst[i] = (float)v * (1.0f / 2147483648.0f);
        }
        break;
    case S24_3:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, src += stride) {
            int32_t v = (int32_t)((uint32_t)src[0] << 8 | (uint32_t)src[1] << 16 | (uint32_t)src[2] << 24) >> 8;
            dst[i] = (float)v * (1.0f / 8388608.0f);
        }
        break;
    case S24:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, src += stride) {
            uint32_t u;
            memcpy(&u, src, 4);
            dst[i] = (float)((int32_t)(u << 8) >> 8) * (1.0f / 8388608.0f);
        }
        break;
    case S16:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, src += stride) {
            int16_t v;
            memcpy(&v, src, 2);
            dst[i] = (float)v * (1.0f / 32768.0f);
        }
        break;
    case F32:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, src += stride) memcpy(&dst[i], src, 4);
        break;
    }
}

static inline float clamp_unit(float x)
{
    if (!(x > -1.0f)) return x != x ? 0.0f : -1.0f; /* NaN plays as silence */
    return x < 1.0f ? x : 1.0f;
}

static inline int32_t round_to_int(double v) { return (int32_t)(v >= 0 ? v + 0.5 : v - 0.5); }

static void from_float(const float *src, uint8_t *dst, size_t stride, snd_pcm_uframes_t frames, sample_kind kind)
{
    switch (kind) {
    case S32:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, dst += stride) {
            int32_t v = round_to_int(clamp_unit(src[i]) * 2147483647.0);
            memcpy(dst, &v, 4);
        }
        break;
    case S24_3:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, dst += stride) {
            int32_t v = round_to_int(clamp_unit(src[i]) * 8388607.0);
            dst[0] = (uint8_t)v;
            dst[1] = (uint8_t)(v >> 8);
            dst[2] = (uint8_t)(v >> 16);
        }
        break;
    case S24:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, dst += stride) {
            int32_t v = round_to_int(clamp_unit(src[i]) * 8388607.0);
            memcpy(dst, &v, 4);
        }
        break;
    case S16:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, dst += stride) {
            int16_t v = (int16_t)round_to_int(clamp_unit(src[i]) * 32767.0);
            memcpy(dst, &v, 2);
        }
        break;
    case F32:
        for (snd_pcm_uframes_t i = 0; i < frames; i++, dst += stride) memcpy(dst, &src[i], 4);
        break;
    }
}

/* Where channel c of frame `frame` starts in a period buffer, and the step to the next frame. */
static uint8_t *sample_at(const pcm_layout *l, snd_pcm_uframes_t period, int32_t c, snd_pcm_uframes_t frame, size_t *stride)
{
    if (l->noninterleaved) {
        *stride = (size_t)l->bytes;
        return l->raw + ((size_t)c * period + frame) * (size_t)l->bytes;
    }
    *stride = (size_t)l->bytes * (size_t)l->channels;
    return l->raw + (frame * (size_t)l->channels + (size_t)c) * (size_t)l->bytes;
}

/* ---- hardware setup ----------------------------------------------------------------------------------------- */

/* 2 periods, 3 on USB interfaces (their URB scheduling wants the slack, as with JACK's -n 3); CTH_ALSA_PERIODS wins. */
static unsigned default_periods(snd_pcm_t *pcm)
{
    const char *env = getenv("CTH_ALSA_PERIODS");
    if (env && atoi(env) >= 2 && atoi(env) <= 8) return (unsigned)atoi(env);
    snd_pcm_info_t *info;
    snd_pcm_info_alloca(&info);
    unsigned periods = 2;
    if (snd_pcm_info(pcm, info) == 0 && snd_pcm_info_get_card(info) >= 0) {
        char ctl_name[32];
        snprintf(ctl_name, sizeof ctl_name, "hw:%d", snd_pcm_info_get_card(info));
        snd_ctl_t *ctl = NULL;
        if (snd_ctl_open(&ctl, ctl_name, 0) == 0) {
            snd_ctl_card_info_t *card;
            snd_ctl_card_info_alloca(&card);
            if (snd_ctl_card_info(ctl, card) == 0 && strcmp(snd_ctl_card_info_get_driver(card), "USB-Audio") == 0) periods = 3;
            snd_ctl_close(ctl);
        }
    }
    return periods;
}

/* Negotiates one direction. The first (playback) gets the nearest rate and period to those asked for; the second
 * (capture, exact) must match them. Channels: all the device has; a plugin PCM with no real limit gets `hint`. */
static int setup_pcm(pcm_layout *l, const char *name, unsigned hint, int exact, unsigned *rate,
                     snd_pcm_uframes_t *period, unsigned *periods)
{
    snd_pcm_t *pcm = l->pcm;
    snd_pcm_hw_params_t *hw;
    snd_pcm_hw_params_alloca(&hw);
    int err = snd_pcm_hw_params_any(pcm, hw);
    if (err < 0) return fail_alsa("no hardware configuration for", name, err);

    if (snd_pcm_hw_params_set_access(pcm, hw, SND_PCM_ACCESS_RW_INTERLEAVED) == 0) l->noninterleaved = 0;
    else if (snd_pcm_hw_params_set_access(pcm, hw, SND_PCM_ACCESS_RW_NONINTERLEAVED) == 0) l->noninterleaved = 1;
    else return cth_linux_fail("%s supports neither interleaved nor non-interleaved read/write access", name);

    size_t f = 0;
    while (f < sizeof formats / sizeof formats[0] && snd_pcm_hw_params_set_format(pcm, hw, formats[f].format) < 0) f++;
    if (f == sizeof formats / sizeof formats[0]) return cth_linux_fail("%s offers no S32, S24, S16 or float format", name);
    l->kind = formats[f].kind;
    l->bytes = formats[f].bytes;

    unsigned channels = 0;
    snd_pcm_hw_params_get_channels_max(hw, &channels);
    if (channels > MAX_DEVICE_CHANNELS) {
        channels = hint > 0 ? hint : 2;
        err = snd_pcm_hw_params_set_channels_near(pcm, hw, &channels);
    } else {
        err = snd_pcm_hw_params_set_channels(pcm, hw, channels);
    }
    if (err < 0 || channels == 0) return fail_alsa("could not set the channel count of", name, err);
    l->channels = (int32_t)channels;

    int dir = 0;
    snd_pcm_hw_params_set_rate_resample(pcm, hw, 0); /* plughw: never resample behind our back */
    err = exact ? snd_pcm_hw_params_set_rate(pcm, hw, *rate, 0) : snd_pcm_hw_params_set_rate_near(pcm, hw, rate, &dir);
    if (err < 0) return cth_linux_fail("%s cannot run at %u Hz (%s)", name, *rate, snd_strerror(err));

    snd_pcm_hw_params_set_periods_integer(pcm, hw);
    dir = 0;
    err = exact ? snd_pcm_hw_params_set_period_size(pcm, hw, *period, 0)
                : snd_pcm_hw_params_set_period_size_near(pcm, hw, period, &dir);
    if (err < 0) return cth_linux_fail("%s cannot use %lu-frame periods (%s)", name, (unsigned long)*period, snd_strerror(err));
    dir = 0;
    err = snd_pcm_hw_params_set_periods_near(pcm, hw, periods, &dir);
    if (err < 0) return fail_alsa("could not set the period count of", name, err);

    err = snd_pcm_hw_params(pcm, hw);
    if (err < 0) return fail_alsa("could not configure", name, err);
    snd_pcm_hw_params_get_rate(hw, rate, &dir);
    snd_pcm_hw_params_get_period_size(hw, period, &dir);
    snd_pcm_hw_params_get_periods(hw, periods, &dir);
    snd_pcm_uframes_t buffer = 0;
    snd_pcm_hw_params_get_buffer_size(hw, &buffer);

    /* Started explicitly (after priming), stopped by an xrun, woken once a whole period is ready. */
    snd_pcm_sw_params_t *sw;
    snd_pcm_sw_params_alloca(&sw);
    snd_pcm_uframes_t boundary = 0;
    snd_pcm_sw_params_current(pcm, sw);
    snd_pcm_sw_params_get_boundary(sw, &boundary);
    snd_pcm_sw_params_set_start_threshold(pcm, sw, boundary);
    snd_pcm_sw_params_set_stop_threshold(pcm, sw, buffer);
    snd_pcm_sw_params_set_avail_min(pcm, sw, *period);
    err = snd_pcm_sw_params(pcm, sw);
    if (err < 0) return fail_alsa("could not set the software parameters of", name, err);

    int n = snd_pcm_poll_descriptors_count(pcm);
    if (n <= 0 || n > MAX_POLL_FDS) return cth_linux_fail("%s has an unexpected number of poll descriptors (%d)", name, n);
    l->nfds = snd_pcm_poll_descriptors(pcm, l->fds, (unsigned)n);
    return 0;
}

static void destroy(cth_stream *s)
{
    if (s->in.pcm) {
        if (s->linked) snd_pcm_unlink(s->in.pcm);
        snd_pcm_close(s->in.pcm);
    }
    if (s->out.pcm) snd_pcm_close(s->out.pcm);
    if (s->wake[0] >= 0) close(s->wake[0]);
    if (s->wake[1] >= 0) close(s->wake[1]);
    free(s->in.raw);
    free(s->out.raw);
    free(s->silence);
    free(s->planar);
    free(s);
}

static int32_t open_alsa(cth_stream *s, const char *name, cth_stream_config *config)
{
    int err = snd_pcm_open(&s->out.pcm, name, SND_PCM_STREAM_PLAYBACK, SND_PCM_NONBLOCK);
    if (err == -EBUSY) {
        return cth_linux_fail("audio device %s is busy (another program, PulseAudio or PipeWire has it open); "
                              "stop that, or use a jack: device", name);
    }
    if (err < 0) return fail_alsa("could not open", name, err);

    unsigned rate = config->sample_rate > 0 ? (unsigned)(config->sample_rate + 0.5) : 48000;
    snd_pcm_uframes_t period = config->buffer_frames > 0 ? (snd_pcm_uframes_t)config->buffer_frames : 256;
    unsigned periods = default_periods(s->out.pcm);
    if (setup_pcm(&s->out, name, 2, 0, &rate, &period, &periods) < 0) return -1;

    int32_t inputs = 0;
    if (config->input_channels > 0 && snd_pcm_open(&s->in.pcm, name, SND_PCM_STREAM_CAPTURE, SND_PCM_NONBLOCK) == 0) {
        /* The capture buffer adds no latency (it is read as soon as a period is in), so give it room for jitter. */
        unsigned capture_periods = periods < 4 ? 4 : periods;
        if (setup_pcm(&s->in, name, (unsigned)config->input_channels, 1, &rate, &period, &capture_periods) < 0) {
            char reason[256];
            snprintf(reason, sizeof reason, "%s", last_error);
            return cth_linux_fail("capture and playback of %s cannot share %u Hz and %lu-frame periods: %s", name,
                                  rate, (unsigned long)period, reason);
        }
        s->linked = snd_pcm_link(s->in.pcm, s->out.pcm) == 0;
        inputs = config->input_channels < s->in.channels ? config->input_channels : s->in.channels;
        if (inputs > CTH_MAX_CHANNELS) inputs = CTH_MAX_CHANNELS;
    }

    int32_t outputs = s->out.channels < CTH_MAX_CHANNELS ? s->out.channels : CTH_MAX_CHANNELS;
    s->period = period;
    s->periods = periods;
    s->timeout_ms = (int)(4000.0 * (double)(period * periods) / rate);
    if (s->timeout_ms < 200) s->timeout_ms = 200;

    s->out.raw = calloc(period * (size_t)s->out.channels, (size_t)s->out.bytes);
    s->silence = calloc(period * (size_t)s->out.channels, (size_t)s->out.bytes);
    s->in.raw = s->in.pcm ? calloc(period * (size_t)s->in.channels, (size_t)s->in.bytes) : NULL;
    s->planar = calloc((size_t)(inputs + outputs) * CTH_MAX_FRAMES, sizeof(float));
    if (!s->out.raw || !s->silence || (s->in.pcm && !s->in.raw) || !s->planar) return cth_linux_fail("out of memory");
    if (pipe2(s->wake, O_NONBLOCK | O_CLOEXEC) < 0) return cth_linux_fail("could not create the wake pipe");
    s->out.fds[s->out.nfds] = (struct pollfd){.fd = s->wake[0], .events = POLLIN};
    if (s->in.pcm) s->in.fds[s->in.nfds] = (struct pollfd){.fd = s->wake[0], .events = POLLIN};

    cth_common *c = &s->common;
    for (int32_t i = 0; i < inputs; i++) c->in_ptrs[i] = s->planar + (size_t)i * CTH_MAX_FRAMES;
    for (int32_t i = 0; i < outputs; i++) c->out_ptrs[i] = s->planar + (size_t)(inputs + i) * CTH_MAX_FRAMES;
    c->input_channels = inputs;
    c->output_channels = outputs;
    c->buffer_frames = (int32_t)period;
    c->sample_rate = rate;
    c->input_latency = inputs > 0 ? (int32_t)period : 0;
    c->output_latency = (int32_t)(period * (periods - 1));
    return 0;
}

int32_t cth_audio_open(const char *device_uid, cth_stream_config *config, cth_process_fn process, void *user, cth_stream **stream)
{
    if (!config || !process || !stream) return cth_linux_fail("invalid arguments");
    *stream = NULL;
    cth_linux_alsa_init();

    char target[256];
    int jack = 0;
    if (resolve(device_uid, target, sizeof target, &jack) < 0) return -1;

    cth_stream *s = (cth_stream *)calloc(1, sizeof *s);
    if (!s) return cth_linux_fail("out of memory");
    s->wake[0] = s->wake[1] = -1;
    s->common.process = process;
    s->common.user = user;

    int32_t result = jack ? cth_jack_open(target, config, &s->common, &s->jack) : open_alsa(s, target, config);
    if (result < 0) {
        destroy(s);
        return -1;
    }

    atomic_store(&backend_is_jack, jack);
    config->sample_rate = s->common.sample_rate;
    config->buffer_frames = s->common.buffer_frames;
    config->input_channels = s->common.input_channels;
    config->output_channels = s->common.output_channels;
    *stream = s;
    return 0;
}

/* ---- the real-time thread ----------------------------------------------------------------------------------- */

/* Waits until the PCM can transfer, cth_audio_stop wakes us (STOPPED), or the device stalls (-ETIMEDOUT). */
static int wait_pcm(cth_stream *s, pcm_layout *l)
{
    int r = poll(l->fds, (nfds_t)l->nfds + 1, s->timeout_ms);
    if (r == 0) return -ETIMEDOUT;
    if (r < 0) return errno == EINTR ? 0 : -errno;
    if (l->fds[l->nfds].revents) return STOPPED;
    unsigned short revents = 0;
    snd_pcm_poll_descriptors_revents(l->pcm, l->fds, (unsigned)l->nfds, &revents);
    if (revents & (POLLERR | POLLNVAL)) {
        switch (snd_pcm_state(l->pcm)) {
        case SND_PCM_STATE_SUSPENDED: return -ESTRPIPE;
        case SND_PCM_STATE_DISCONNECTED: return -ENODEV;
        default: return -EPIPE;
        }
    }
    return 0;
}

/* Moves one period between the device and l->raw (or plays `silence` instead), waiting as needed. */
static int transfer(cth_stream *s, pcm_layout *l, int capture, const uint8_t *silence)
{
    snd_pcm_uframes_t done = 0, period = s->period;
    const uint8_t *base = silence ? silence : l->raw;
    while (done < period) {
        snd_pcm_sframes_t r;
        if (l->noninterleaved) {
            for (int32_t c = 0; c < l->channels; c++) {
                l->areas[c] = (uint8_t *)base + ((silence ? 0 : (size_t)c * period) + done) * (size_t)l->bytes;
            }
            r = capture ? snd_pcm_readn(l->pcm, l->areas, period - done) : snd_pcm_writen(l->pcm, l->areas, period - done);
        } else {
            uint8_t *at = (uint8_t *)base + done * (size_t)l->channels * (size_t)l->bytes;
            r = capture ? snd_pcm_readi(l->pcm, at, period - done) : snd_pcm_writei(l->pcm, at, period - done);
        }

        if (r == -EAGAIN || r == 0) {
            int w = wait_pcm(s, l);
            if (w) return w;
            continue;
        }
        if (r < 0) return (int)r;
        done += (snd_pcm_uframes_t)r;
    }
    return 0;
}

/* (Re)starts the streams from scratch: after start and after every xrun, so input and output stay a fixed distance
 * apart. Playback is primed with a full buffer of silence; starting it starts the linked capture. */
static int restart(cth_stream *s)
{
    snd_pcm_drop(s->out.pcm);
    if (s->in.pcm) snd_pcm_drop(s->in.pcm);
    int err = snd_pcm_prepare(s->out.pcm);
    if (err < 0) return err;
    if (s->in.pcm && (err = snd_pcm_prepare(s->in.pcm)) < 0) return err;

    snd_pcm_sframes_t room = snd_pcm_avail_update(s->out.pcm);
    if (room < 0) return (int)room;
    while (room >= (snd_pcm_sframes_t)s->period) {
        if ((err = transfer(s, &s->out, 0, s->silence)) != 0) return err;
        room -= (snd_pcm_sframes_t)s->period;
    }

    if ((err = snd_pcm_start(s->out.pcm)) < 0) return err;
    if (s->in.pcm && !s->linked && (err = snd_pcm_start(s->in.pcm)) < 0) return err;
    return 0;
}

/* One period: capture, convert, process (in CTH_MAX_FRAMES slices), convert, play. */
static int cycle(cth_stream *s)
{
    cth_common *c = &s->common;
    int err;
    if (s->in.pcm && (err = transfer(s, &s->in, 1, NULL)) != 0) return err;

    cth_linux_begin_cycle(c);
    uint64_t started = cth_linux_now_ns();
    for (snd_pcm_uframes_t offset = 0; offset < s->period; offset += CTH_MAX_FRAMES) {
        snd_pcm_uframes_t frames = s->period - offset < CTH_MAX_FRAMES ? s->period - offset : CTH_MAX_FRAMES;
        size_t stride;
        for (int32_t ch = 0; ch < c->input_channels; ch++) {
            const uint8_t *src = sample_at(&s->in, s->period, ch, offset, &stride);
            to_float(src, stride, (float *)c->in_ptrs[ch], frames, s->in.kind);
        }
        for (int32_t ch = 0; ch < c->output_channels; ch++) memset(c->out_ptrs[ch], 0, frames * sizeof(float));
        c->process(c->user, c->in_ptrs, c->input_channels, c->out_ptrs, c->output_channels, (int32_t)frames);
        for (int32_t ch = 0; ch < c->output_channels; ch++) {
            uint8_t *dst = sample_at(&s->out, s->period, ch, offset, &stride);
            from_float(c->out_ptrs[ch], dst, stride, frames, s->out.kind);
        }
    }
    cth_linux_end_cycle(c, started, (int32_t)s->period);

    return transfer(s, &s->out, 0, NULL);
}

static void *audio_thread(void *arg)
{
    cth_stream *s = (cth_stream *)arg;
    cth_fpu_flush_denormals();
    int err = restart(s);
    while (atomic_load_explicit(&s->running, memory_order_relaxed)) {
        if (err == 0) err = cycle(s);
        if (err == 0) continue;
        if (err == STOPPED) break;

        /* An xrun, a stall or a suspend: count it as a dropout and start over. */
        atomic_fetch_add_explicit(&s->common.overloads, 1, memory_order_relaxed);
        if (err == -ENODEV) {
            fprintf(stderr, "cth_audio: the audio device was disconnected\n");
            cth_linux_raise_status(&s->common, CTH_STREAM_LOST);
            break;
        }
        err = restart(s);
        if (err == -ENODEV) { /* gone while restarting after an xrun */
            fprintf(stderr, "cth_audio: the audio device was disconnected\n");
            cth_linux_raise_status(&s->common, CTH_STREAM_LOST);
            break;
        }
        if (err < 0) {
            /* Not recoverable right now (for example mid-suspend): retry a few times a second until stopped. */
            struct pollfd wake = {.fd = s->wake[0], .events = POLLIN};
            if (poll(&wake, 1, 200) > 0) break;
        }
    }
    return NULL;
}

/* SCHED_FIFO at CTH_RT_PRIORITY (default 70, 0 turns it off) when the process may use it, else normal priority. */
static int start_thread(cth_stream *s)
{
    int priority = DEFAULT_PRIORITY;
    const char *env = getenv("CTH_RT_PRIORITY");
    if (env) priority = atoi(env);
    int max = sched_get_priority_max(SCHED_FIFO);
    if (priority > max) priority = max;

    int err = -1;
    if (priority > 0) {
        pthread_attr_t attr;
        struct sched_param param = {.sched_priority = priority};
        pthread_attr_init(&attr);
        pthread_attr_setinheritsched(&attr, PTHREAD_EXPLICIT_SCHED);
        pthread_attr_setschedpolicy(&attr, SCHED_FIFO);
        pthread_attr_setschedparam(&attr, &param);
        err = pthread_create(&s->thread, &attr, audio_thread, s);
        pthread_attr_destroy(&attr);
        if (err != 0) {
            fprintf(stderr,
                    "cth_audio: real-time scheduling (SCHED_FIFO %d) is not permitted (%s), so the audio thread runs at "
                    "normal priority and may drop out under load. Allow it with LimitRTPRIO= in the systemd unit or an "
                    "rtprio limit in /etc/security/limits.d.\n",
                    priority, strerror(err));
        }
    }
    if (err != 0) err = pthread_create(&s->thread, NULL, audio_thread, s);
    if (err == 0) pthread_setname_np(s->thread, "cth-audio");
    return err;
}

/* Locks the process's memory once, as pages are touched (MCL_ONFAULT), so a page the audio path has used is never
 * paged out and a major fault cannot stall the real-time thread. On fault only: the looper's large buffers stay lazily
 * committed. CTH_MLOCK=0 turns it off. Without permission (no LimitMEMLOCK / memlock limit) it warns and goes on. */
static void lock_memory(void)
{
    static atomic_flag done = ATOMIC_FLAG_INIT;
    if (atomic_flag_test_and_set(&done)) return;
    const char *env = getenv("CTH_MLOCK");
    if (env && strcmp(env, "0") == 0) return;
#ifdef MCL_ONFAULT
    if (mlockall(MCL_CURRENT | MCL_FUTURE | MCL_ONFAULT) == 0) return;
#else
    errno = EINVAL; /* only locking everything up front is available, which would commit the looper's buffers */
#endif
    fprintf(stderr,
            "cth_audio: memory could not be locked (%s), so the audio thread may stall on a page fault under memory "
            "pressure. Allow it with LimitMEMLOCK=infinity in the systemd unit or a memlock limit in "
            "/etc/security/limits.d.\n",
            strerror(errno));
}

int32_t cth_audio_start(cth_stream *s)
{
    if (!s) return cth_linux_fail("no stream");
    lock_memory();
    if (s->jack) return cth_jack_start(s->jack);
    if (atomic_load(&s->running)) return 0;

    int err = snd_pcm_prepare(s->out.pcm);
    if (err < 0) return fail_alsa("could not prepare", "the playback stream", err);
    char drain[16];
    while (read(s->wake[0], drain, sizeof drain) > 0) {}
    atomic_store(&s->running, 1);
    err = start_thread(s);
    if (err != 0) {
        atomic_store(&s->running, 0);
        return cth_linux_fail("could not start the audio thread (%s)", strerror(err));
    }
    s->thread_started = 1;
    return 0;
}

int32_t cth_audio_stop(cth_stream *s)
{
    if (!s) return 0;
    if (s->jack) return cth_jack_stop(s->jack);
    if (!s->thread_started) return 0;
    atomic_store(&s->running, 0);
    ssize_t written = write(s->wake[1], "x", 1);
    (void)written;
    pthread_join(s->thread, NULL);
    s->thread_started = 0;
    snd_pcm_drop(s->out.pcm);
    if (s->in.pcm) snd_pcm_drop(s->in.pcm);
    return 0;
}

void cth_audio_close(cth_stream *s)
{
    if (!s) return;
    cth_audio_stop(s);
    if (s->jack) cth_jack_close(s->jack);
    destroy(s);
}

void cth_audio_stats(const cth_stream *s, cth_stream_stats *stats)
{
    if (!stats) return;
    memset(stats, 0, sizeof *stats);
    if (!s) return;
    stats->callbacks = atomic_load_explicit(&((cth_stream *)s)->common.callbacks, memory_order_relaxed);
    stats->overloads = atomic_load_explicit(&((cth_stream *)s)->common.overloads, memory_order_relaxed);
    stats->input_latency_frames = s->common.input_latency;
    stats->output_latency_frames = s->common.output_latency;
}

void cth_audio_timing(const cth_stream *s, cth_stream_timing *timing)
{
    if (!timing) return;
    if (!s) {
        memset(timing, 0, sizeof *timing);
        return;
    }
    memcpy(timing, &s->common.timing, sizeof *timing); /* racy snapshot of counters; fine for statistics */
}

void cth_audio_reset_timing(cth_stream *s)
{
    if (s) atomic_store_explicit(&s->common.reset_timing, 1, memory_order_release);
}

int32_t cth_audio_status(const cth_stream *s)
{
    return s ? atomic_load_explicit(&((cth_stream *)s)->common.status, memory_order_relaxed) : CTH_STREAM_OK;
}
