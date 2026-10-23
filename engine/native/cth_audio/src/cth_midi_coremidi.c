/* SPDX-License-Identifier: GPL-3.0-or-later
 * CoreMIDI input and output for cth_audio (see cth_audio.h). Uses the MIDI 1.0 protocol through MIDIEventList, so
 * incoming words are Universal MIDI Packets: type 1 (system) and type 2 (MIDI 1.0 channel voice) are decoded. */
#include <CoreMIDI/CoreMIDI.h>
#include <mach/mach_time.h>
#include <stdatomic.h>
#include <stdio.h>
#include <string.h>
#include "cth_audio.h"

#define RING_SIZE 4096 /* power of two */

static MIDIClientRef client;
static MIDIPortRef input_port;
static MIDIPortRef output_port;
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

int32_t cth_midi_port_count(void) { return (int32_t)(MIDIGetNumberOfSources() + MIDIGetNumberOfDestinations()); }

int32_t cth_midi_port_info(int32_t index, cth_midi_port *port)
{
    if (!port || index < 0) return -1;
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
    if (!client) {
        if (MIDIClientCreate(CFSTR("Cathode"), NULL, NULL, &client) != noErr) return -1;
        if (MIDIInputPortCreateWithProtocol(client, CFSTR("Cathode In"), kMIDIProtocol_1_0, &input_port,
                                            ^(const MIDIEventList *list, void *source) { receive(list, source); })
            != noErr) return -2;
        if (MIDIOutputPortCreate(client, CFSTR("Cathode Out"), &output_port) != noErr) return -3;
    }

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
    if (!output_port || !data || length < 1 || length > 3) return -1;
    uint32_t word = (0x2u << 28) | ((uint32_t)data[0] << 16) | (length > 1 ? (uint32_t)data[1] << 8 : 0)
                    | (length > 2 ? data[2] : 0);
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

void cth_midi_close(void)
{
    if (client) {
        MIDIClientDispose(client);
        client = 0;
        input_port = 0;
        output_port = 0;
    }
}
