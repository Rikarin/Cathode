/* SPDX-License-Identifier: GPL-3.0-or-later
 * WinMM MIDI input and output for cth_audio (see cth_audio.h). WinMM numbers ports by position, which shifts when
 * devices come and go, so uids are "in:" or "out:" + the port name + "#n" for the n-th port of that name: stable
 * across runs as long as the same devices are plugged in. Inputs call back on a WinMM thread; events are timestamped
 * there on the QueryPerformanceCounter clock and queued in a ring that cth_midi_read drains. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mmsystem.h>
#include <stdio.h>
#include <string.h>
#include "cth_audio.h"

#define RING_SIZE 4096 /* power of two */
#define MAX_PORTS 64

static HMIDIIN inputs[MAX_PORTS];
static int input_count;
static HMIDIOUT outputs[MAX_PORTS]; /* opened on first send, by WinMM index */
static int opened;
static SRWLOCK ports_lock = SRWLOCK_INIT;   /* the handle tables */
static SRWLOCK producer_lock = SRWLOCK_INIT; /* input callbacks may run on more than one thread */
static cth_midi_event ring[RING_SIZE];
static volatile LONG ring_head; /* written by the WinMM callbacks */
static volatile LONG ring_tail; /* written by the reader */

uint64_t cth_midi_now_ns(void)
{
    static LARGE_INTEGER frequency;
    LARGE_INTEGER now;
    if (frequency.QuadPart == 0) QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&now);
    /* Split to avoid overflowing 64 bits for long uptimes. */
    uint64_t seconds = (uint64_t)now.QuadPart / (uint64_t)frequency.QuadPart;
    uint64_t rest = (uint64_t)now.QuadPart % (uint64_t)frequency.QuadPart;
    return seconds * 1000000000u + rest * 1000000000u / (uint64_t)frequency.QuadPart;
}

static void push(uint64_t time_ns, uint8_t status, uint8_t d1, uint8_t d2, uint8_t length)
{
    AcquireSRWLockExclusive(&producer_lock);
    uint32_t head = (uint32_t)ring_head;
    uint32_t tail = (uint32_t)InterlockedCompareExchange(&ring_tail, 0, 0);
    if (head - tail < RING_SIZE) { /* full: drop */
        cth_midi_event *e = &ring[head & (RING_SIZE - 1)];
        e->time_ns = time_ns;
        e->data[0] = status;
        e->data[1] = d1;
        e->data[2] = d2;
        e->length = length;
        InterlockedExchange(&ring_head, (LONG)(head + 1)); /* full barrier: the event is visible first */
    }
    ReleaseSRWLockExclusive(&producer_lock);
}

static uint8_t channel_length(uint8_t status)
{
    switch (status & 0xF0) {
    case 0xC0:
    case 0xD0: return 2;
    default: return 3;
    }
}

static void CALLBACK receive(HMIDIIN handle, UINT message, DWORD_PTR instance, DWORD_PTR param1, DWORD_PTR param2)
{
    (void)handle; (void)instance; (void)param2;
    if (message != MIM_DATA) return; /* SysEx (MIM_LONGDATA) is dropped, as on macOS */
    uint8_t status = (uint8_t)(param1 & 0xFF);
    uint64_t time = cth_midi_now_ns();
    if (status >= 0x80 && status < 0xF0) {
        push(time, status, (uint8_t)((param1 >> 8) & 0x7F), (uint8_t)((param1 >> 16) & 0x7F), channel_length(status));
    } else if (status >= 0xF8) {
        push(time, status, 0, 0, 1); /* clock, start, continue, stop, … */
    }
}

static void port_name(int is_input, UINT index, char *out, size_t size)
{
    wchar_t wide[MAXPNAMELEN];
    MMRESULT result;
    if (is_input) {
        MIDIINCAPSW caps;
        result = midiInGetDevCapsW(index, &caps, sizeof caps);
        memcpy(wide, caps.szPname, sizeof wide);
    } else {
        MIDIOUTCAPSW caps;
        result = midiOutGetDevCapsW(index, &caps, sizeof caps);
        memcpy(wide, caps.szPname, sizeof wide);
    }
    wide[MAXPNAMELEN - 1] = 0;
    if (result != MMSYSERR_NOERROR || !WideCharToMultiByte(CP_UTF8, 0, wide, -1, out, (int)size, NULL, NULL)) out[0] = 0;
}

/* Writes "<prefix><name>#<n>" for port index of the given kind, n counting earlier ports with the same name. */
static void port_uid(int is_input, UINT index, char *uid, size_t uid_size, char *name, size_t name_size)
{
    char current[256], earlier[256];
    int duplicates = 0;
    port_name(is_input, index, current, sizeof current);
    for (UINT i = 0; i < index; i++) {
        port_name(is_input, i, earlier, sizeof earlier);
        if (strcmp(earlier, current) == 0) duplicates++;
    }
    if (name) snprintf(name, name_size, "%s", current);
    snprintf(uid, uid_size, "%s%s#%d", is_input ? "in:" : "out:", current, duplicates);
}

int32_t cth_midi_port_count(void) { return (int32_t)(midiInGetNumDevs() + midiOutGetNumDevs()); }

int32_t cth_midi_port_info(int32_t index, cth_midi_port *port)
{
    if (!port || index < 0) return -1;
    UINT sources = midiInGetNumDevs();
    UINT destinations = midiOutGetNumDevs();
    if ((UINT)index >= sources + destinations) return -1;
    port->is_input = (UINT)index < sources;
    UINT i = port->is_input ? (UINT)index : (UINT)index - sources;
    port_uid(port->is_input, i, port->uid, sizeof port->uid, port->name, sizeof port->name);
    return 0;
}

static void close_all(void)
{
    for (int i = 0; i < input_count; i++) {
        if (!inputs[i]) continue;
        midiInStop(inputs[i]);
        midiInReset(inputs[i]);
        midiInClose(inputs[i]);
        inputs[i] = NULL;
    }
    input_count = 0;
    for (int i = 0; i < MAX_PORTS; i++) {
        if (!outputs[i]) continue;
        midiOutReset(outputs[i]);
        midiOutClose(outputs[i]);
        outputs[i] = NULL;
    }
}

int32_t cth_midi_open(void)
{
    AcquireSRWLockExclusive(&ports_lock);
    close_all(); /* indices shift when devices come and go, so start over */
    UINT count = midiInGetNumDevs();
    if (count > MAX_PORTS) count = MAX_PORTS;
    for (UINT i = 0; i < count; i++) {
        /* A port another application holds exclusively stays closed; the rest still work. */
        if (midiInOpen(&inputs[i], i, (DWORD_PTR)receive, 0, CALLBACK_FUNCTION) == MMSYSERR_NOERROR) midiInStart(inputs[i]);
        else inputs[i] = NULL;
    }
    input_count = (int)count;
    opened = 1;
    ReleaseSRWLockExclusive(&ports_lock);
    return 0;
}

int32_t cth_midi_read(cth_midi_event *events, int32_t max)
{
    uint32_t tail = (uint32_t)ring_tail;
    uint32_t head = (uint32_t)InterlockedCompareExchange(&ring_head, 0, 0);
    int32_t n = 0;
    while (tail != head && n < max) {
        events[n++] = ring[tail & (RING_SIZE - 1)];
        tail++;
    }

    InterlockedExchange(&ring_tail, (LONG)tail);
    return n;
}

int32_t cth_midi_send(const char *destination_uid, const uint8_t *data, int32_t length)
{
    if (!opened || !data || length < 1 || length > 3) return -1;
    DWORD message = data[0] | (length > 1 ? (DWORD)data[1] << 8 : 0) | (length > 2 ? (DWORD)data[2] << 16 : 0);

    int32_t sent = 0;
    AcquireSRWLockExclusive(&ports_lock);
    UINT count = midiOutGetNumDevs();
    if (count > MAX_PORTS) count = MAX_PORTS;
    for (UINT i = 0; i < count; i++) {
        if (destination_uid) {
            char uid[64];
            port_uid(0, i, uid, sizeof uid, NULL, 0);
            if (strcmp(uid, destination_uid) != 0) continue;
        }
        if (!outputs[i] && midiOutOpen(&outputs[i], i, 0, 0, CALLBACK_NULL) != MMSYSERR_NOERROR) {
            outputs[i] = NULL;
            continue;
        }
        if (midiOutShortMsg(outputs[i], message) == MMSYSERR_NOERROR) sent++;
    }
    ReleaseSRWLockExclusive(&ports_lock);
    return sent > 0 ? 0 : -3;
}

void cth_midi_close(void)
{
    AcquireSRWLockExclusive(&ports_lock);
    close_all();
    opened = 0;
    ReleaseSRWLockExclusive(&ports_lock);
}
