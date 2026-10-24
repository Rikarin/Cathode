/* SPDX-License-Identifier: GPL-3.0-or-later
 * ALSA sequencer MIDI input and output for cth_audio (see cth_audio.h). The sequencer, unlike rawmidi, shares ports
 * with other programs and covers USB, serial and software ports alike. Incoming events are stamped on arrival by a
 * dedicated reader thread blocked in poll() (CLOCK_MONOTONIC, the cth_midi_now_ns clock), decoded to MIDI bytes and
 * queued in the same lock-free ring as the CoreMIDI version.
 *
 * Port uids are "<client name>:<port number>" ("FBV Express Mk II:0"): sequencer client numbers depend on the order
 * devices were plugged in, names and port numbers do not. A second identical device gets "#2" appended. */
#define _GNU_SOURCE
#include <alsa/asoundlib.h>
#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

#include "cth_audio.h"
#include "cth_audio_linux.h"

#define RING_SIZE 4096 /* power of two */
#define MAX_PORTS 128
#define MAX_POLL_FDS 8

typedef struct port_entry {
    cth_midi_port info;
    int client;
    int port;
} port_entry;

/* lock guards the client, the port tables and output; the reader thread only takes events in, without it. */
static pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;
static snd_seq_t *seq;
static int self_client = -1;
static int input_port = -1;
static int output_port = -1;
static snd_midi_event_t *decoder;
static snd_midi_event_t *encoder;
static pthread_t reader;
static int wake[2] = {-1, -1};
static port_entry ports[MAX_PORTS]; /* what cth_midi_port_count listed */
static int32_t port_total;
static port_entry destinations[MAX_PORTS]; /* rescanned for each send */
static cth_midi_event ring[RING_SIZE];
static _Atomic uint32_t ring_head; /* written by the reader thread */
static _Atomic uint32_t ring_tail; /* written by cth_midi_read */

uint64_t cth_midi_now_ns(void) { return cth_linux_now_ns(); }

static void push(uint64_t time_ns, uint8_t status, uint8_t d1, uint8_t d2, uint8_t length)
{
    uint32_t head = atomic_load_explicit(&ring_head, memory_order_relaxed);
    uint32_t tail = atomic_load_explicit(&ring_tail, memory_order_acquire);
    if (head - tail >= RING_SIZE) return; /* full: drop */
    cth_midi_event *e = &ring[head & (RING_SIZE - 1)];
    e->time_ns = time_ns;
    e->data[0] = status;
    e->data[1] = d1;
    e->data[2] = d2;
    e->length = length;
    atomic_store_explicit(&ring_head, head + 1, memory_order_release);
}

static uint8_t channel_length(uint8_t status)
{
    switch (status & 0xF0) {
    case 0xC0:
    case 0xD0: return 2;
    default: return 3;
    }
}

/* One sequencer event can decode to several messages (a 14-bit controller is two, an RPN four). Channel messages
 * and system real-time are queued; SysEx and system common are dropped, as with CoreMIDI. */
static void receive(const snd_seq_event_t *event, uint64_t time)
{
    if (event->type == SND_SEQ_EVENT_SYSEX) return;
    unsigned char bytes[16];
    long length = snd_midi_event_decode(decoder, bytes, sizeof bytes, event);
    for (long i = 0; i < length;) {
        uint8_t status = bytes[i];
        if (status >= 0xF8) {
            push(time, status, 0, 0, 1);
            i++;
        } else if (status >= 0x80 && status < 0xF0 && i + channel_length(status) <= length) {
            uint8_t n = channel_length(status);
            push(time, status, bytes[i + 1] & 0x7F, n > 2 ? bytes[i + 2] & 0x7F : 0, n);
            i += n;
        } else {
            break;
        }
    }
}

static void *read_midi(void *arg)
{
    (void)arg;
    struct pollfd fds[MAX_POLL_FDS + 1];
    int n = snd_seq_poll_descriptors(seq, fds, MAX_POLL_FDS, POLLIN);
    fds[n] = (struct pollfd){.fd = wake[0], .events = POLLIN};
    for (;;) {
        if (poll(fds, (nfds_t)n + 1, -1) < 0 && errno != EINTR) break;
        if (fds[n].revents) break;
        uint64_t time = cth_midi_now_ns();
        snd_seq_event_t *event = NULL;
        int r;
        while ((r = snd_seq_event_input(seq, &event)) >= 0 || r == -ENOSPC) {
            if (r >= 0 && event) receive(event, time);
            event = NULL;
        }
    }
    return NULL;
}

static int skipped_client(int client, const char *name)
{
    /* The system ports (timer, announce), our own, the kernel's loopback (sending to it would come straight back in)
     * and PipeWire's bridge ports, which mirror the hardware ones. */
    return client == SND_SEQ_CLIENT_SYSTEM || client == self_client || strcmp(name, "Midi Through") == 0
           || strncmp(name, "PipeWire-", 9) == 0;
}

/* Lists sources (ports we can read from), then destinations, like CoreMIDI. Returns how many were stored. */
static int32_t scan(snd_seq_t *handle, port_entry *out, int32_t max)
{
    snd_seq_client_info_t *client_info;
    snd_seq_port_info_t *port_info;
    snd_seq_client_info_alloca(&client_info);
    snd_seq_port_info_alloca(&port_info);
    int32_t total = 0;
    for (int is_input = 1; is_input >= 0; is_input--) {
        unsigned need = is_input ? SND_SEQ_PORT_CAP_READ | SND_SEQ_PORT_CAP_SUBS_READ
                                 : SND_SEQ_PORT_CAP_WRITE | SND_SEQ_PORT_CAP_SUBS_WRITE;
        snd_seq_client_info_set_client(client_info, -1);
        while (snd_seq_query_next_client(handle, client_info) >= 0) {
            int client = snd_seq_client_info_get_client(client_info);
            const char *client_name = snd_seq_client_info_get_name(client_info);
            if (skipped_client(client, client_name)) continue;
            snd_seq_port_info_set_client(port_info, client);
            snd_seq_port_info_set_port(port_info, -1);
            while (snd_seq_query_next_port(handle, port_info) >= 0 && total < max) {
                unsigned caps = snd_seq_port_info_get_capability(port_info);
                if ((caps & need) != need || (caps & SND_SEQ_PORT_CAP_NO_EXPORT)) continue;
                port_entry *e = &out[total];
                memset(e, 0, sizeof *e);
                e->client = client;
                e->port = snd_seq_port_info_get_port(port_info);
                e->info.is_input = is_input;
                const char *port_name = snd_seq_port_info_get_name(port_info);
                if (strncmp(port_name, client_name, strlen(client_name)) == 0) {
                    snprintf(e->info.name, sizeof e->info.name, "%s", port_name);
                } else {
                    snprintf(e->info.name, sizeof e->info.name, "%s: %s", client_name, port_name);
                }

                char uid[48];
                snprintf(uid, sizeof uid, "%.32s:%d", client_name, e->port);
                snprintf(e->info.uid, sizeof e->info.uid, "%s", uid);
                for (int copy = 2; copy < 100; copy++) {
                    int32_t clash = 0;
                    for (int32_t i = 0; i < total && !clash; i++) {
                        clash = out[i].info.is_input == is_input && strcmp(out[i].info.uid, e->info.uid) == 0;
                    }
                    if (!clash) break;
                    snprintf(e->info.uid, sizeof e->info.uid, "%s#%d", uid, copy);
                }
                total++;
            }
        }
    }
    return total;
}

/* Runs scan on our client, or on a short-lived one before cth_midi_open. Call with the lock held. */
static int32_t scan_any(port_entry *out, int32_t max)
{
    if (seq) return scan(seq, out, max);
    cth_linux_alsa_init();
    snd_seq_t *handle = NULL;
    if (snd_seq_open(&handle, "default", SND_SEQ_OPEN_DUPLEX, SND_SEQ_NONBLOCK) < 0) return 0;
    int32_t total = scan(handle, out, max);
    snd_seq_close(handle);
    return total;
}

int32_t cth_midi_port_count(void)
{
    pthread_mutex_lock(&lock);
    port_total = scan_any(ports, MAX_PORTS);
    int32_t total = port_total;
    pthread_mutex_unlock(&lock);
    return total;
}

int32_t cth_midi_port_info(int32_t index, cth_midi_port *port)
{
    if (!port || index < 0) return -1;
    pthread_mutex_lock(&lock);
    if (port_total == 0) port_total = scan_any(ports, MAX_PORTS);
    int32_t ok = index < port_total;
    if (ok) *port = ports[index].info;
    pthread_mutex_unlock(&lock);
    return ok ? 0 : -1;
}

static void close_client(void)
{
    if (decoder) snd_midi_event_free(decoder);
    if (encoder) snd_midi_event_free(encoder);
    if (seq) snd_seq_close(seq);
    if (wake[0] >= 0) close(wake[0]);
    if (wake[1] >= 0) close(wake[1]);
    decoder = encoder = NULL;
    seq = NULL;
    wake[0] = wake[1] = -1;
    self_client = input_port = output_port = -1;
}

static int32_t create_client(void)
{
    cth_linux_alsa_init();
    if (snd_seq_open(&seq, "default", SND_SEQ_OPEN_DUPLEX, SND_SEQ_NONBLOCK) < 0) {
        seq = NULL;
        return -1;
    }
    snd_seq_set_client_name(seq, "Cathode");
    self_client = snd_seq_client_id(seq);
    input_port = snd_seq_create_simple_port(seq, "Cathode In", SND_SEQ_PORT_CAP_WRITE | SND_SEQ_PORT_CAP_SUBS_WRITE,
                                            SND_SEQ_PORT_TYPE_MIDI_GENERIC | SND_SEQ_PORT_TYPE_APPLICATION);
    if (input_port < 0) return -2;
    output_port = snd_seq_create_simple_port(seq, "Cathode Out", SND_SEQ_PORT_CAP_READ | SND_SEQ_PORT_CAP_SUBS_READ,
                                             SND_SEQ_PORT_TYPE_MIDI_GENERIC | SND_SEQ_PORT_TYPE_APPLICATION);
    if (output_port < 0) return -3;
    if (snd_midi_event_new(256, &decoder) < 0 || snd_midi_event_new(16, &encoder) < 0) return -4;
    snd_midi_event_no_status(decoder, 1); /* every decoded message carries its status byte */
    if (pipe2(wake, O_NONBLOCK | O_CLOEXEC) < 0) return -5;
    if (pthread_create(&reader, NULL, read_midi, NULL) != 0) return -6;
    pthread_setname_np(reader, "cth-midi");
    return 0;
}

int32_t cth_midi_open(void)
{
    pthread_mutex_lock(&lock);
    if (!seq) {
        int32_t err = create_client();
        if (err < 0) {
            close_client();
            pthread_mutex_unlock(&lock);
            return err;
        }
    }

    /* Subscribing a source twice fails with EBUSY, which is fine: the refresh picks up new devices. */
    port_total = scan(seq, ports, MAX_PORTS);
    for (int32_t i = 0; i < port_total; i++) {
        if (ports[i].info.is_input) snd_seq_connect_from(seq, input_port, ports[i].client, ports[i].port);
    }
    pthread_mutex_unlock(&lock);
    return 0;
}

int32_t cth_midi_read(cth_midi_event *events, int32_t max)
{
    uint32_t tail = atomic_load_explicit(&ring_tail, memory_order_relaxed);
    uint32_t head = atomic_load_explicit(&ring_head, memory_order_acquire);
    int32_t n = 0;
    while (tail != head && n < max) {
        events[n++] = ring[tail & (RING_SIZE - 1)];
        tail++;
    }

    atomic_store_explicit(&ring_tail, tail, memory_order_release);
    return n;
}

int32_t cth_midi_send(const char *destination_uid, const uint8_t *data, int32_t length)
{
    if (!data || length < 1 || length > 3) return -1;
    pthread_mutex_lock(&lock);
    if (!seq) {
        pthread_mutex_unlock(&lock);
        return -1;
    }

    snd_seq_event_t event;
    snd_seq_ev_clear(&event);
    snd_midi_event_reset_encode(encoder);
    long used = snd_midi_event_encode(encoder, data, length, &event);
    if (used <= 0 || event.type == SND_SEQ_EVENT_NONE) {
        pthread_mutex_unlock(&lock);
        return -2;
    }
    snd_seq_ev_set_source(&event, output_port);
    snd_seq_ev_set_direct(&event);

    int32_t sent = 0;
    int32_t count = scan(seq, destinations, MAX_PORTS);
    for (int32_t i = 0; i < count; i++) {
        if (destinations[i].info.is_input) continue;
        if (destination_uid && strcmp(destinations[i].info.uid, destination_uid) != 0) continue;
        snd_seq_ev_set_dest(&event, destinations[i].client, destinations[i].port);
        if (snd_seq_event_output_direct(seq, &event) >= 0) sent++;
    }
    pthread_mutex_unlock(&lock);
    return sent > 0 ? 0 : -3;
}

void cth_midi_close(void)
{
    pthread_mutex_lock(&lock);
    if (seq) {
        ssize_t written = write(wake[1], "x", 1);
        (void)written;
        pthread_join(reader, NULL);
        close_client();
    }
    pthread_mutex_unlock(&lock);
}
