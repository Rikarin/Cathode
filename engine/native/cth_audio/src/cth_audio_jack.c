/*
 * JACK backend for device ids prefixed "jack:" (jackd, or PipeWire's JACK on desktop distributions). libjack is
 * loaded with dlopen on first use, so the shim neither builds nor runs against it unless a JACK server is wanted.
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * A device is a JACK client that owns physical ports: "system" under jackd, one client per card under PipeWire. The
 * stream registers one port per channel and connects it to that client's physical ports in order. JACK's process
 * thread is already real-time and its buffers are planar float, so the callback reads and writes them in place.
 * Rate and buffer size are the server's; a stream cannot change them.
 */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "cth_audio.h"
#include "cth_audio_linux.h"

/* The parts of <jack/jack.h> used here (the JACK ABI has been stable since 2008). */
typedef uint32_t jack_nframes_t;
typedef struct jack_client jack_client_t;
typedef struct jack_port jack_port_t;
typedef struct jack_latency_range {
    jack_nframes_t min;
    jack_nframes_t max;
} jack_latency_range_t;

enum { JackNoStartServer = 0x01 };
enum { JackPortIsInput = 0x1, JackPortIsOutput = 0x2, JackPortIsPhysical = 0x4 };
enum { JackCaptureLatency = 0, JackPlaybackLatency = 1 };
#define JACK_DEFAULT_AUDIO_TYPE "32 bit float mono audio"
#define MAX_JACK_PORTS 256

static struct {
    int loaded; /* 1 loaded, -1 unavailable */
    jack_client_t *(*client_open)(const char *, int, int *, ...);
    int (*client_close)(jack_client_t *);
    jack_nframes_t (*get_sample_rate)(jack_client_t *);
    jack_nframes_t (*get_buffer_size)(jack_client_t *);
    jack_port_t *(*port_register)(jack_client_t *, const char *, const char *, unsigned long, unsigned long);
    void *(*port_get_buffer)(jack_port_t *, jack_nframes_t);
    const char *(*port_name)(const jack_port_t *);
    jack_port_t *(*port_by_name)(jack_client_t *, const char *);
    void (*port_get_latency_range)(jack_port_t *, int, jack_latency_range_t *);
    int (*set_process_callback)(jack_client_t *, int (*)(jack_nframes_t, void *), void *);
    int (*set_xrun_callback)(jack_client_t *, int (*)(void *), void *);
    void (*on_shutdown)(jack_client_t *, void (*)(void *), void *);
    const char **(*get_ports)(jack_client_t *, const char *, const char *, unsigned long);
    void (*free)(void *);
    int (*connect)(jack_client_t *, const char *, const char *);
    int (*activate)(jack_client_t *);
    int (*deactivate)(jack_client_t *);
    void (*set_error_function)(void (*)(const char *));
    void (*set_info_function)(void (*)(const char *));
} jack;

struct cth_jack_stream {
    cth_common *common;
    jack_client_t *client;
    char target[128];
    int32_t chunk; /* frames per process call: the buffer size the stream was opened with, at most CTH_MAX_FRAMES */
    jack_port_t *inputs[CTH_MAX_CHANNELS];
    jack_port_t *outputs[CTH_MAX_CHANNELS];
    int active;
};

static void quiet(const char *message) { (void)message; }

/* memcpy: ISO C has no conversion from void * to a function pointer, but POSIX guarantees the representation. */
#define LOAD(field, symbol)                                                                                          \
    do {                                                                                                             \
        void *address = dlsym(library, symbol);                                                                      \
        if (!address) return 0;                                                                                      \
        memcpy(&jack.field, &address, sizeof address);                                                               \
    } while (0)

static int bind_symbols(void *library)
{
    LOAD(client_open, "jack_client_open");
    LOAD(client_close, "jack_client_close");
    LOAD(get_sample_rate, "jack_get_sample_rate");
    LOAD(get_buffer_size, "jack_get_buffer_size");
    LOAD(port_register, "jack_port_register");
    LOAD(port_get_buffer, "jack_port_get_buffer");
    LOAD(port_name, "jack_port_name");
    LOAD(port_by_name, "jack_port_by_name");
    LOAD(port_get_latency_range, "jack_port_get_latency_range");
    LOAD(set_process_callback, "jack_set_process_callback");
    LOAD(set_xrun_callback, "jack_set_xrun_callback");
    LOAD(on_shutdown, "jack_on_shutdown");
    LOAD(get_ports, "jack_get_ports");
    LOAD(free, "jack_free");
    LOAD(connect, "jack_connect");
    LOAD(activate, "jack_activate");
    LOAD(deactivate, "jack_deactivate");
    LOAD(set_error_function, "jack_set_error_function");
    LOAD(set_info_function, "jack_set_info_function");
    return 1;
}

static int load(void)
{
    if (jack.loaded) return jack.loaded > 0;
    void *library = dlopen("libjack.so.0", RTLD_NOW | RTLD_LOCAL);
    if (!library || !bind_symbols(library)) {
        if (library) dlclose(library);
        jack.loaded = -1;
        return 0;
    }
    jack.set_error_function(quiet); /* "cannot connect to server" is the normal answer when none runs */
    jack.set_info_function(quiet);
    jack.loaded = 1;
    return 1;
}

static jack_client_t *connect_client(void)
{
    if (!load()) return NULL;
    int status = 0;
    return jack.client_open("Cathode", JackNoStartServer, &status);
}

/* The physical ports of one direction, optionally only those of one client ("system:" prefix). */
static int32_t physical_ports(jack_client_t *client, unsigned long direction, const char *owner, const char **out,
                              int32_t max, const char ***list)
{
    *list = jack.get_ports(client, NULL, JACK_DEFAULT_AUDIO_TYPE, JackPortIsPhysical | direction);
    int32_t n = 0;
    size_t length = owner ? strlen(owner) : 0;
    for (const char **p = *list; p && *p && n < max; p++) {
        if (!owner || (strncmp(*p, owner, length) == 0 && (*p)[length] == ':')) out[n++] = *p;
    }
    return n;
}

int32_t cth_jack_devices(cth_device_info *devices, int32_t max)
{
    jack_client_t *client = connect_client();
    if (!client) return 0;
    double rate = jack.get_sample_rate(client);
    int32_t count = 0;
    for (int direction = 0; direction < 2; direction++) {
        /* Capture ports are JACK outputs (they produce audio); playback ports are JACK inputs. */
        const char **list = NULL, *names[MAX_JACK_PORTS];
        int32_t n = physical_ports(client, direction == 0 ? JackPortIsOutput : JackPortIsInput, NULL, names,
                                   MAX_JACK_PORTS, &list);
        for (int32_t i = 0; i < n; i++) {
            const char *colon = strchr(names[i], ':');
            if (!colon) continue;
            char owner[128];
            snprintf(owner, sizeof owner, "%.*s", (int)(colon - names[i]), names[i]);
            int32_t d = 0;
            while (d < count && strcmp(devices[d].uid + 5, owner) != 0) d++;
            if (d == count) {
                if (count == max) continue;
                memset(&devices[d], 0, sizeof devices[d]);
                snprintf(devices[d].uid, sizeof devices[d].uid, "jack:%s", owner);
                snprintf(devices[d].name, sizeof devices[d].name, "JACK: %s", owner);
                devices[d].sample_rate = rate;
                count++;
            }
            if (direction == 0) devices[d].input_channels++;
            else devices[d].output_channels++;
        }
        if (list) jack.free(list);
    }
    jack.client_close(client);
    return count;
}

static int process(jack_nframes_t nframes, void *arg)
{
    static _Thread_local int fpu_configured;
    cth_jack_stream *j = (cth_jack_stream *)arg;
    cth_common *c = j->common;
    if (!fpu_configured) {
        cth_fpu_flush_denormals();
        fpu_configured = 1;
    }

    cth_linux_begin_cycle(c);
    uint64_t started = cth_linux_now_ns();
    const float *in[CTH_MAX_CHANNELS];
    float *out[CTH_MAX_CHANNELS];
    for (int32_t ch = 0; ch < c->input_channels; ch++) in[ch] = (const float *)jack.port_get_buffer(j->inputs[ch], nframes);
    for (int32_t ch = 0; ch < c->output_channels; ch++) out[ch] = (float *)jack.port_get_buffer(j->outputs[ch], nframes);

    /* A server whose buffer grew since the stream opened still gets callbacks of the size the engine was built for. */
    for (jack_nframes_t offset = 0; offset < nframes; offset += (jack_nframes_t)j->chunk) {
        jack_nframes_t frames = nframes - offset < (jack_nframes_t)j->chunk ? nframes - offset : (jack_nframes_t)j->chunk;
        for (int32_t ch = 0; ch < c->input_channels; ch++) c->in_ptrs[ch] = in[ch] + offset;
        for (int32_t ch = 0; ch < c->output_channels; ch++) {
            c->out_ptrs[ch] = out[ch] + offset;
            memset(c->out_ptrs[ch], 0, frames * sizeof(float));
        }
        c->process(c->user, c->in_ptrs, c->input_channels, c->out_ptrs, c->output_channels, (int32_t)frames);
    }

    cth_linux_end_cycle(c, started, (int32_t)nframes);
    return 0;
}

static int xrun(void *arg)
{
    atomic_fetch_add_explicit(&((cth_jack_stream *)arg)->common->overloads, 1, memory_order_relaxed);
    return 0;
}

static void shutdown_server(void *arg)
{
    cth_jack_stream *j = (cth_jack_stream *)arg;
    j->active = 0;
    atomic_fetch_add_explicit(&j->common->overloads, 1, memory_order_relaxed);
    cth_linux_raise_status(j->common, CTH_STREAM_LOST); /* the server quit or threw the client out */
}

static int32_t latency(jack_port_t *port, int mode)
{
    jack_latency_range_t range = {0, 0};
    if (port) jack.port_get_latency_range(port, mode, &range);
    return (int32_t)range.max;
}

int32_t cth_jack_open(const char *target, cth_stream_config *config, cth_common *common, cth_jack_stream **stream)
{
    *stream = NULL;
    if (!load()) return cth_linux_fail("JACK is not installed (libjack.so.0 not found)");
    jack_client_t *client = connect_client();
    if (!client) return cth_linux_fail("no JACK server is running");

    cth_jack_stream *j = (cth_jack_stream *)calloc(1, sizeof *j);
    if (!j) {
        jack.client_close(client);
        return cth_linux_fail("out of memory");
    }
    j->common = common;
    j->client = client;
    snprintf(j->target, sizeof j->target, "%s", target[0] ? target : "system");

    const char **capture_list = NULL, **playback_list = NULL, *capture[MAX_JACK_PORTS], *playback[MAX_JACK_PORTS];
    int32_t capture_count = physical_ports(client, JackPortIsOutput, j->target, capture, MAX_JACK_PORTS, &capture_list);
    int32_t playback_count = physical_ports(client, JackPortIsInput, j->target, playback, MAX_JACK_PORTS, &playback_list);
    int32_t outputs = playback_count < CTH_MAX_CHANNELS ? playback_count : CTH_MAX_CHANNELS;
    int32_t inputs = config->input_channels < capture_count ? config->input_channels : capture_count;
    if (inputs < 0) inputs = 0;
    if (inputs > CTH_MAX_CHANNELS) inputs = CTH_MAX_CHANNELS;
    common->input_latency = inputs > 0 ? latency(jack.port_by_name(client, capture[0]), JackCaptureLatency) : 0;
    common->output_latency = outputs > 0 ? latency(jack.port_by_name(client, playback[0]), JackPlaybackLatency) : 0;
    if (capture_list) jack.free(capture_list);
    if (playback_list) jack.free(playback_list);
    if (outputs <= 0) {
        cth_jack_close(j);
        return cth_linux_fail("JACK client %s has no physical playback ports", j->target);
    }

    char name[32];
    for (int32_t ch = 0; ch < inputs; ch++) {
        snprintf(name, sizeof name, "in_%d", (int)ch + 1);
        j->inputs[ch] = jack.port_register(client, name, JACK_DEFAULT_AUDIO_TYPE, JackPortIsInput, 0);
    }
    for (int32_t ch = 0; ch < outputs; ch++) {
        snprintf(name, sizeof name, "out_%d", (int)ch + 1);
        j->outputs[ch] = jack.port_register(client, name, JACK_DEFAULT_AUDIO_TYPE, JackPortIsOutput, 0);
    }
    for (int32_t ch = 0; ch < inputs; ch++) if (!j->inputs[ch]) inputs = ch;
    for (int32_t ch = 0; ch < outputs; ch++) if (!j->outputs[ch]) outputs = ch;
    if (outputs == 0) {
        cth_jack_close(j);
        return cth_linux_fail("could not register JACK ports");
    }

    jack.set_process_callback(client, process, j);
    jack.set_xrun_callback(client, xrun, j);
    jack.on_shutdown(client, shutdown_server, j);

    jack_nframes_t buffer = jack.get_buffer_size(client);
    j->chunk = buffer > 0 && buffer < CTH_MAX_FRAMES ? (int32_t)buffer : CTH_MAX_FRAMES;
    common->input_channels = inputs;
    common->output_channels = outputs;
    common->buffer_frames = (int32_t)buffer;
    common->sample_rate = jack.get_sample_rate(client);
    *stream = j;
    return 0;
}

int32_t cth_jack_start(cth_jack_stream *j)
{
    if (j->active) return 0;
    if (jack.activate(j->client) != 0) return cth_linux_fail("could not activate the JACK client");
    j->active = 1;

    /* Ports connect only once the client is active. A connection that fails leaves that channel silent. */
    const char **capture_list = NULL, **playback_list = NULL, *capture[MAX_JACK_PORTS], *playback[MAX_JACK_PORTS];
    int32_t capture_count = physical_ports(j->client, JackPortIsOutput, j->target, capture, MAX_JACK_PORTS, &capture_list);
    int32_t playback_count = physical_ports(j->client, JackPortIsInput, j->target, playback, MAX_JACK_PORTS, &playback_list);
    for (int32_t ch = 0; ch < j->common->input_channels && ch < capture_count; ch++) {
        jack.connect(j->client, capture[ch], jack.port_name(j->inputs[ch]));
    }
    for (int32_t ch = 0; ch < j->common->output_channels && ch < playback_count; ch++) {
        jack.connect(j->client, jack.port_name(j->outputs[ch]), playback[ch]);
    }
    if (capture_list) jack.free(capture_list);
    if (playback_list) jack.free(playback_list);
    return 0;
}

int32_t cth_jack_stop(cth_jack_stream *j)
{
    if (!j->active) return 0;
    j->active = 0;
    return jack.deactivate(j->client) == 0 ? 0 : cth_linux_fail("could not deactivate the JACK client");
}

void cth_jack_close(cth_jack_stream *j)
{
    if (!j) return;
    cth_jack_stop(j);
    jack.client_close(j->client);
    free(j);
}
