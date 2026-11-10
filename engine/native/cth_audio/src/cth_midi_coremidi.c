/* SPDX-License-Identifier: GPL-3.0-or-later
 * CoreMIDI input and output for cth_audio (see cth_audio.h). Uses the MIDI 1.0 protocol through MIDIEventList, so
 * incoming words are Universal MIDI Packets: type 1 (system) and type 2 (MIDI 1.0 channel voice) are decoded.
 *
 * CoreMIDI keeps a process's view of the MIDI setup up to date on the run loop of the thread that made the process's
 * first CoreMIDI call. Without a run loop there, MIDIGetNumberOfSources and the rest keep answering with the setup as
 * it was at that first call, so a device plugged in later is never seen. The host has no run loop of its own, so every
 * entry point first starts a thread (once, for the life of the process) that creates the client and then runs one. */
#include <CoreMIDI/CoreMIDI.h>
#include <dispatch/dispatch.h>
#include <mach/mach_time.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdio.h>
#include <string.h>
#include "cth_audio.h"

#define RING_SIZE 4096 /* power of two */

static MIDIClientRef client;
static MIDIPortRef input_port;
static MIDIPortRef output_port;
static pthread_once_t client_once = PTHREAD_ONCE_INIT;
static dispatch_semaphore_t client_ready;
static int32_t client_error;          /* 0, or why the client could not be created */
static _Atomic int opened;            /* between cth_midi_open and cth_midi_close */
static _Atomic uint32_t generation;   /* bumped by CoreMIDI's setup-changed notification */
static cth_midi_event ring[RING_SIZE];
static _Atomic uint32_t ring_head; /* written by the CoreMIDI thread */
static _Atomic uint32_t ring_tail; /* written by the reader */
static mach_timebase_info_data_t timebase;

static uint64_t to_ns(uint64_t host_time)
{
    if (timebase.denom == 0) mach_timebase_info(&timebase);
    return host_time * timebase.numer / timebase.denom;
}

uint64_t cth_midi_now_ns(void) { return to_ns(mach_absolute_time()); }

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

static void receive(const MIDIEventList *list, void *source)
{
    (void)source;
    const MIDIEventPacket *packet = &list->packet[0];
    for (UInt32 p = 0; p < list->numPackets; p++) {
        uint64_t time = packet->timeStamp ? to_ns(packet->timeStamp) : cth_midi_now_ns();
        for (UInt32 w = 0; w < packet->wordCount; w++) {
            uint32_t word = packet->words[w];
            uint32_t type = word >> 28;
            uint8_t status = (word >> 16) & 0xFF;
            if (type == 0x2) {
                push(time, status, (word >> 8) & 0x7F, word & 0x7F, channel_length(status));
            } else if (type == 0x1 && status >= 0xF8) {
                push(time, status, 0, 0, 1); /* clock, start, continue, stop, … */
            } else if (type == 0x3 || type == 0x4 || type == 0x5) {
                w += (type == 0x3) ? 1 : (type == 0x4 ? 1 : 3); /* skip SysEx and 64/128-bit packets */
            }
        }

        packet = MIDIEventPacketNext(packet);
    }
}

static void copy_string(MIDIObjectRef object, CFStringRef property, char *out, size_t size)
{
    CFStringRef value = NULL;
    out[0] = 0;
    if (MIDIObjectGetStringProperty(object, property, &value) == noErr && value) {
        CFStringGetCString(value, out, (CFIndex)size, kCFStringEncodingUTF8);
        CFRelease(value);
    }
}

static int32_t create_client(void)
{
    if (MIDIClientCreateWithBlock(CFSTR("Cathode"), &client, ^(const MIDINotification *notification) {
            if (notification->messageID == kMIDIMsgSetupChanged) atomic_fetch_add(&generation, 1);
        }) != noErr) return -1;
    if (MIDIInputPortCreateWithProtocol(client, CFSTR("Cathode In"), kMIDIProtocol_1_0, &input_port,
                                        ^(const MIDIEventList *list, void *source) { receive(list, source); })
        != noErr) return -2;
    if (MIDIOutputPortCreate(client, CFSTR("Cathode Out"), &output_port) != noErr) return -3;
    return 0;
}

/* The process's first CoreMIDI call, then the run loop CoreMIDI needs to follow devices coming and going. */
static void *run_client(void *arg)
{
    (void)arg;
    pthread_setname_np("cth-midi");
    client_error = create_client();
    dispatch_semaphore_signal(client_ready);

    /* A run loop with nothing to wait on returns at once: this source keeps it running. */
    CFRunLoopSourceContext context = {0};
    CFRunLoopSourceRef keep_alive = CFRunLoopSourceCreate(NULL, 0, &context);
    CFRunLoopAddSource(CFRunLoopGetCurrent(), keep_alive, kCFRunLoopDefaultMode);
    CFRunLoopRun();
    return NULL;
}

static void start_client(void)
{
    pthread_t thread;
    client_ready = dispatch_semaphore_create(0);
    if (pthread_create(&thread, NULL, run_client, NULL) != 0) {
        client_error = -4;
        return;
    }

    pthread_detach(thread);
    dispatch_semaphore_wait(client_ready, DISPATCH_TIME_FOREVER);
}

/* Call before any other CoreMIDI function. Returns 0, or a negative number when there is no client. */
static int32_t ensure_client(void)
{
    pthread_once(&client_once, start_client);
    return client_error;
}

int32_t cth_midi_port_count(void)
{
    ensure_client();
    return (int32_t)(MIDIGetNumberOfSources() + MIDIGetNumberOfDestinations());
}

int32_t cth_midi_port_info(int32_t index, cth_midi_port *port)
{
    if (!port || index < 0) return -1;
    ensure_client();
    ItemCount sources = MIDIGetNumberOfSources();
    MIDIEndpointRef endpoint = (ItemCount)index < sources ? MIDIGetSource((ItemCount)index)
                                                          : MIDIGetDestination((ItemCount)index - sources);
    if (!endpoint) return -1;
    SInt32 unique = 0;
    MIDIObjectGetIntegerProperty(endpoint, kMIDIPropertyUniqueID, &unique);
    snprintf(port->uid, sizeof port->uid, "%d", (int)unique);
    copy_string(endpoint, kMIDIPropertyDisplayName, port->name, sizeof port->name);
    port->is_input = (ItemCount)index < sources;
    return 0;
}

int32_t cth_midi_open(void)
{
    int32_t err = ensure_client();
    if (err < 0) return err;
    atomic_store(&opened, 1);
    ItemCount count = MIDIGetNumberOfSources();
    for (ItemCount i = 0; i < count; i++) {
        MIDIEndpointRef source = MIDIGetSource(i);
        MIDIPortDisconnectSource(input_port, source); /* reconnecting is harmless; refresh picks up new devices */
        MIDIPortConnectSource(input_port, source, NULL);
    }

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
    if (!atomic_load(&opened) || !data || length < 1 || length > 3 || data[0] < 0x80) return -1;
    /* A Universal MIDI Packet names its kind in the top nibble: 2 is a MIDI 1.0 channel voice message, 1 is system
     * common and real-time (clock, start, song position, ...). Sent as type 2, a system message is malformed. */
    uint32_t type = data[0] >= 0xF0 ? 0x1u : 0x2u;
    uint32_t word = (type << 28) | ((uint32_t)data[0] << 16) | (length > 1 ? (uint32_t)(data[1] & 0x7F) << 8 : 0)
                    | (length > 2 ? (uint32_t)(data[2] & 0x7F) : 0);
    MIDIEventList list;
    MIDIEventPacket *packet = MIDIEventListInit(&list, kMIDIProtocol_1_0);
    packet = MIDIEventListAdd(&list, sizeof list, packet, 0, 1, &word);
    if (!packet) return -2;

    int32_t sent = 0;
    ItemCount count = MIDIGetNumberOfDestinations();
    for (ItemCount i = 0; i < count; i++) {
        MIDIEndpointRef destination = MIDIGetDestination(i);
        if (destination_uid) {
            SInt32 unique = 0;
            char uid[64];
            MIDIObjectGetIntegerProperty(destination, kMIDIPropertyUniqueID, &unique);
            snprintf(uid, sizeof uid, "%d", (int)unique);
            if (strcmp(uid, destination_uid) != 0) continue;
        }

        if (MIDISendEventList(output_port, destination, &list) == noErr) sent++;
    }

    return sent > 0 ? 0 : -3;
}

uint32_t cth_midi_generation(void) { return atomic_load(&generation); }

/* The client and its thread stay for the life of the process (CoreMIDI is tied to that thread's run loop, see the
 * top of the file); closing disconnects the sources, so nothing more is queued. */
void cth_midi_close(void)
{
    if (!atomic_exchange(&opened, 0)) return;
    ItemCount count = MIDIGetNumberOfSources();
    for (ItemCount i = 0; i < count; i++) MIDIPortDisconnectSource(input_port, MIDIGetSource(i));
}
