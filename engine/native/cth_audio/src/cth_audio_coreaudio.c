/*
 * CoreAudio backend: a HAL IOProc on one device. This is the lowest-latency path on macOS; the IOProc
 * thread is already real-time, so no workgroup join is needed for the callback itself.
 *
 * Property listeners are blocks on the stream's own serial dispatch queue: removing a listener does not wait for a
 * notification the HAL has already dispatched, so close drains that queue before it frees the stream.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <Block.h>
#include <CoreAudio/CoreAudio.h>
#include <dispatch/dispatch.h>
#include <mach/mach_time.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "cth_audio.h"

#define MAX_CHANNELS 64

struct cth_stream {
    AudioObjectID device;
    AudioDeviceIOProcID proc_id;
    cth_process_fn process;
    void *user;
    int32_t input_channels;
    int32_t output_channels;
    int32_t buffer_frames;
    int32_t input_latency;
    int32_t output_latency;
    _Atomic uint64_t callbacks;
    _Atomic uint64_t overloads;
    int running;
    double sample_rate;
    double ticks_to_ns;
    _Atomic int reset_timing; /* set by the control thread, applied by the audio thread */
    _Atomic int status;       /* CTH_STREAM_*, raised by the property listeners */
    int follows_default;      /* opened with a NULL uid: a new default output invalidates the stream */
    cth_stream_timing timing;  /* written only by the audio thread */
    float *planar; /* (inputs + outputs) * CTH_MAX_FRAMES */
    dispatch_queue_t listeners;                   /* serial queue every property listener runs on */
    AudioObjectPropertyListenerBlock on_device;   /* Block_copy'd, so close can remove the same blocks */
    AudioObjectPropertyListenerBlock on_system;
    const float *in_ptrs[MAX_CHANNELS];
    float *out_ptrs[MAX_CHANNELS];
};

static _Thread_local char last_error[256];
static _Thread_local int fpu_configured;

static int32_t fail(const char *what, OSStatus status)
{
    snprintf(last_error, sizeof last_error, "%s (OSStatus %d)", what, (int)status);
    return -1;
}

const char *cth_audio_last_error(void) { return last_error; }
const char *cth_audio_backend(void) { return "coreaudio"; }

static AudioObjectPropertyAddress address(AudioObjectPropertySelector selector, AudioObjectPropertyScope scope)
{
    AudioObjectPropertyAddress a = {selector, scope, kAudioObjectPropertyElementMain};
    return a;
}

static int32_t device_list(AudioObjectID **devices)
{
    AudioObjectPropertyAddress a = address(kAudioHardwarePropertyDevices, kAudioObjectPropertyScopeGlobal);
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(kAudioObjectSystemObject, &a, 0, NULL, &size) != noErr) return 0;
    *devices = (AudioObjectID *)malloc(size);
    if (!*devices || AudioObjectGetPropertyData(kAudioObjectSystemObject, &a, 0, NULL, &size, *devices) != noErr) {
        free(*devices);
        *devices = NULL;
        return 0;
    }
    return (int32_t)(size / sizeof(AudioObjectID));
}

static int32_t channel_count(AudioObjectID device, AudioObjectPropertyScope scope)
{
    AudioObjectPropertyAddress a = address(kAudioDevicePropertyStreamConfiguration, scope);
    UInt32 size = 0;
    if (AudioObjectGetPropertyDataSize(device, &a, 0, NULL, &size) != noErr || size == 0) return 0;
    AudioBufferList *list = (AudioBufferList *)malloc(size);
    int32_t channels = 0;
    if (list && AudioObjectGetPropertyData(device, &a, 0, NULL, &size, list) == noErr) {
        for (UInt32 i = 0; i < list->mNumberBuffers; i++) channels += (int32_t)list->mBuffers[i].mNumberChannels;
    }
    free(list);
    return channels;
}

static void copy_string_property(AudioObjectID device, AudioObjectPropertySelector selector, char *out, size_t out_size)
{
    AudioObjectPropertyAddress a = address(selector, kAudioObjectPropertyScopeGlobal);
    CFStringRef value = NULL;
    UInt32 size = sizeof value;
    out[0] = 0;
    if (AudioObjectGetPropertyData(device, &a, 0, NULL, &size, &value) == noErr && value) {
        CFStringGetCString(value, out, (CFIndex)out_size, kCFStringEncodingUTF8);
        CFRelease(value);
    }
}

static double nominal_rate(AudioObjectID device)
{
    AudioObjectPropertyAddress a = address(kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal);
    Float64 rate = 0;
    UInt32 size = sizeof rate;
    AudioObjectGetPropertyData(device, &a, 0, NULL, &size, &rate);
    return rate;
}

static UInt32 get_u32(AudioObjectID object, AudioObjectPropertySelector selector, AudioObjectPropertyScope scope)
{
    AudioObjectPropertyAddress a = address(selector, scope);
    UInt32 value = 0, size = sizeof value;
    AudioObjectGetPropertyData(object, &a, 0, NULL, &size, &value);
    return value;
}

static int32_t total_latency(AudioObjectID device, AudioObjectPropertyScope scope, int32_t buffer_frames)
{
    UInt32 latency = get_u32(device, kAudioDevicePropertyLatency, scope);
    UInt32 safety = get_u32(device, kAudioDevicePropertySafetyOffset, scope);
    UInt32 stream_latency = 0;
    AudioObjectPropertyAddress a = address(kAudioDevicePropertyStreams, scope);
    AudioStreamID streams[16];
    UInt32 size = sizeof streams;
    if (AudioObjectGetPropertyData(device, &a, 0, NULL, &size, streams) == noErr && size >= sizeof(AudioStreamID)) {
        stream_latency = get_u32(streams[0], kAudioStreamPropertyLatency, kAudioObjectPropertyScopeGlobal);
    }
    return (int32_t)(latency + safety + stream_latency) + buffer_frames;
}

int32_t cth_audio_device_count(void)
{
    AudioObjectID *devices = NULL;
    int32_t count = device_list(&devices);
    free(devices);
    return count;
}

int32_t cth_audio_device_info(int32_t index, cth_device_info *info)
{
    AudioObjectID *devices = NULL;
    int32_t count = device_list(&devices);
    if (index < 0 || index >= count || !info) {
        free(devices);
        return fail("device index out of range", 0);
    }
    AudioObjectID device = devices[index];
    free(devices);
    copy_string_property(device, kAudioDevicePropertyDeviceUID, info->uid, sizeof info->uid);
    copy_string_property(device, kAudioObjectPropertyName, info->name, sizeof info->name);
    info->input_channels = channel_count(device, kAudioObjectPropertyScopeInput);
    info->output_channels = channel_count(device, kAudioObjectPropertyScopeOutput);
    info->sample_rate = nominal_rate(device);
    return 0;
}

int32_t cth_audio_channel_name(int32_t device_index, int32_t is_input, int32_t channel, char *name, int32_t size)
{
    if (!name || size <= 0) return fail("invalid arguments", 0);
    name[0] = 0;
    AudioObjectID *devices = NULL;
    int32_t count = device_list(&devices);
    if (device_index < 0 || device_index >= count) {
        free(devices);
        return fail("device index out of range", 0);
    }
    AudioObjectID device = devices[device_index];
    free(devices);
    AudioObjectPropertyScope scope = is_input ? kAudioObjectPropertyScopeInput : kAudioObjectPropertyScopeOutput;
    if (channel < 0 || channel >= channel_count(device, scope)) return fail("channel out of range", 0);

    /* Channel elements are 1-based; element 0 is the device as a whole. */
    AudioObjectPropertyAddress a = {kAudioObjectPropertyElementName, scope, (AudioObjectPropertyElement)(channel + 1)};
    CFStringRef value = NULL;
    UInt32 value_size = sizeof value;
    if (AudioObjectHasProperty(device, &a)
        && AudioObjectGetPropertyData(device, &a, 0, NULL, &value_size, &value) == noErr && value) {
        CFStringGetCString(value, name, (CFIndex)size, kCFStringEncodingUTF8);
        CFRelease(value);
    }
    return 0;
}

static AudioObjectID find_device(const char *uid)
{
    if (!uid || !uid[0]) {
        AudioObjectPropertyAddress a = address(kAudioHardwarePropertyDefaultOutputDevice, kAudioObjectPropertyScopeGlobal);
        AudioObjectID device = kAudioObjectUnknown;
        UInt32 size = sizeof device;
        AudioObjectGetPropertyData(kAudioObjectSystemObject, &a, 0, NULL, &size, &device);
        return device;
    }

    AudioObjectID *devices = NULL, found = kAudioObjectUnknown;
    int32_t count = device_list(&devices);
    char candidate[256];
    for (int32_t i = 0; i < count && found == kAudioObjectUnknown; i++) {
        copy_string_property(devices[i], kAudioDevicePropertyDeviceUID, candidate, sizeof candidate);
        if (strcmp(candidate, uid) == 0) found = devices[i];
    }
    free(devices);
    return found;
}

/* Copies the device's interleaved buffers (possibly several streams) into planar channel pointers, or back. */
static void deinterleave(const AudioBufferList *list, const float *const *planar, int32_t channels, UInt32 offset, UInt32 frames)
{
    int32_t channel = 0;
    for (UInt32 b = 0; b < list->mNumberBuffers && channel < channels; b++) {
        const AudioBuffer *buffer = &list->mBuffers[b];
        const float *data = (const float *)buffer->mData;
        UInt32 stride = buffer->mNumberChannels;
        for (UInt32 c = 0; c < stride && channel < channels; c++, channel++) {
            float *dst = (float *)planar[channel];
            for (UInt32 i = 0; i < frames; i++) dst[i] = data ? data[(offset + i) * stride + c] : 0.0f;
        }
    }
    /* Channels the device did not deliver this time (a stream missing from the list) read as silence. */
    for (; channel < channels; channel++) memset((float *)planar[channel], 0, frames * sizeof(float));
}

static void interleave(AudioBufferList *list, float *const *planar, int32_t channels, UInt32 offset, UInt32 frames)
{
    int32_t channel = 0;
    for (UInt32 b = 0; b < list->mNumberBuffers; b++) {
        AudioBuffer *buffer = &list->mBuffers[b];
        float *data = (float *)buffer->mData;
        UInt32 stride = buffer->mNumberChannels;
        if (!data) continue;
        for (UInt32 c = 0; c < stride; c++, channel++) {
            for (UInt32 i = 0; i < frames; i++) {
                data[(offset + i) * stride + c] = channel < channels ? planar[channel][i] : 0.0f;
            }
        }
    }
}

static OSStatus io_proc(AudioObjectID device, const AudioTimeStamp *now, const AudioBufferList *input,
                        const AudioTimeStamp *input_time, AudioBufferList *output, const AudioTimeStamp *output_time,
                        void *client)
{
    (void)device; (void)now; (void)input_time; (void)output_time;
    cth_stream *s = (cth_stream *)client;
    if (!fpu_configured) {
        cth_fpu_flush_denormals();
        fpu_configured = 1;
    }

    if (atomic_exchange_explicit(&s->reset_timing, 0, memory_order_acquire)) memset(&s->timing, 0, sizeof s->timing);
    uint64_t started = mach_absolute_time();

    UInt32 total = 0;
    if (output && output->mNumberBuffers > 0 && output->mBuffers[0].mNumberChannels > 0) {
        total = output->mBuffers[0].mDataByteSize / (output->mBuffers[0].mNumberChannels * sizeof(float));
    }

    for (UInt32 offset = 0; offset < total; offset += CTH_MAX_FRAMES) {
        UInt32 frames = total - offset < CTH_MAX_FRAMES ? total - offset : CTH_MAX_FRAMES;
        if (s->input_channels > 0 && input) {
            deinterleave(input, s->in_ptrs, s->input_channels, offset, frames);
        } else {
            for (int32_t c = 0; c < s->input_channels; c++) memset((float *)s->in_ptrs[c], 0, frames * sizeof(float));
        }
        for (int32_t c = 0; c < s->output_channels; c++) memset(s->out_ptrs[c], 0, frames * sizeof(float));
        s->process(s->user, s->in_ptrs, s->input_channels, s->out_ptrs, s->output_channels, (int32_t)frames);
        interleave(output, s->out_ptrs, s->output_channels, offset, frames);
    }

    if (total > 0 && s->sample_rate > 0) {
        double elapsed_ns = (double)(mach_absolute_time() - started) * s->ticks_to_ns;
        double percent = elapsed_ns / (total / s->sample_rate * 1e9) * 100.0;
        int bin = percent >= CTH_TIMING_BINS - 1 ? CTH_TIMING_BINS - 1 : (int)percent;
        s->timing.bins[bin]++;
        if (percent > s->timing.max_percent) s->timing.max_percent = percent;
        if (percent >= 100.0) s->timing.over_budget++;
    }

    atomic_fetch_add_explicit(&s->callbacks, 1, memory_order_relaxed);
    return noErr;
}

/* Device properties the stream listens to: overloads, and the device-global settings any other application (a DAW,
 * Audio MIDI Setup) can change under it, or the device going away. */
static const AudioObjectPropertyAddress device_properties[] = {
    {kAudioDeviceProcessorOverload, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain},
    {kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain},
    {kAudioDevicePropertyBufferFrameSize, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain},
    {kAudioDevicePropertyDeviceIsAlive, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain},
};
#define DEVICE_PROPERTIES (sizeof device_properties / sizeof device_properties[0])

static const AudioObjectPropertyAddress default_output_address = {
    kAudioHardwarePropertyDefaultOutputDevice, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};

/* Raises the stream status to at least value (it never goes back down). */
static void raise_status(cth_stream *s, int value)
{
    int old = atomic_load_explicit(&s->status, memory_order_relaxed);
    while (old < value && !atomic_compare_exchange_weak(&s->status, &old, value)) {
    }
}

/* Listener queue: what one notification about the stream's device means. */
static void device_changed(cth_stream *s, AudioObjectPropertySelector selector)
{
    switch (selector) {
    case kAudioDeviceProcessorOverload:
        atomic_fetch_add_explicit(&s->overloads, 1, memory_order_relaxed);
        break;
    case kAudioDevicePropertyNominalSampleRate:
        if (nominal_rate(s->device) != s->sample_rate) raise_status(s, CTH_STREAM_CHANGED);
        break;
    case kAudioDevicePropertyBufferFrameSize:
        if ((int32_t)get_u32(s->device, kAudioDevicePropertyBufferFrameSize, kAudioObjectPropertyScopeGlobal)
            != s->buffer_frames)
            raise_status(s, CTH_STREAM_CHANGED);
        break;
    case kAudioDevicePropertyDeviceIsAlive:
        if (!get_u32(s->device, kAudioDevicePropertyDeviceIsAlive, kAudioObjectPropertyScopeGlobal))
            raise_status(s, CTH_STREAM_LOST);
        break;
    default:
        break;
    }
}

static AudioObjectID default_output(void)
{
    AudioObjectID device = kAudioObjectUnknown;
    UInt32 size = sizeof device;
    AudioObjectGetPropertyData(kAudioObjectSystemObject, &default_output_address, 0, NULL, &size, &device);
    return device;
}

/* Registers the stream's property listeners on its serial queue. */
static void add_listeners(cth_stream *s)
{
    s->listeners = dispatch_queue_create("org.cathode.audio.listeners", DISPATCH_QUEUE_SERIAL);
    s->on_device = Block_copy(^(UInt32 count, const AudioObjectPropertyAddress *addresses) {
        for (UInt32 i = 0; i < count; i++) device_changed(s, addresses[i].mSelector);
    });
    for (size_t i = 0; i < DEVICE_PROPERTIES; i++)
        AudioObjectAddPropertyListenerBlock(s->device, &device_properties[i], s->listeners, s->on_device);
    if (s->follows_default) {
        s->on_system = Block_copy(^(UInt32 count, const AudioObjectPropertyAddress *addresses) {
            (void)count; (void)addresses;
            if (default_output() != s->device) raise_status(s, CTH_STREAM_CHANGED);
        });
        AudioObjectAddPropertyListenerBlock(kAudioObjectSystemObject, &default_output_address, s->listeners, s->on_system);
    }
}

/* Removes the listeners, then waits for any notification already queued, so none can touch the stream once freed. */
static void remove_listeners(cth_stream *s)
{
    if (!s->listeners) return;
    for (size_t i = 0; i < DEVICE_PROPERTIES; i++)
        AudioObjectRemovePropertyListenerBlock(s->device, &device_properties[i], s->listeners, s->on_device);
    if (s->on_system)
        AudioObjectRemovePropertyListenerBlock(kAudioObjectSystemObject, &default_output_address, s->listeners, s->on_system);
    dispatch_sync(s->listeners, ^{
    });
    Block_release(s->on_device);
    if (s->on_system) Block_release(s->on_system);
    s->on_device = NULL;
    s->on_system = NULL;
    dispatch_release(s->listeners);
    s->listeners = NULL;
}

static OSStatus set_rate(AudioObjectID device, double rate)
{
    if (rate <= 0 || nominal_rate(device) == rate) return noErr;
    AudioObjectPropertyAddress a = address(kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal);
    Float64 value = rate;
    OSStatus status = AudioObjectSetPropertyData(device, &a, 0, NULL, sizeof value, &value);
    for (int i = 0; status == noErr && i < 100 && nominal_rate(device) != rate; i++) usleep(10000); /* applies asynchronously */
    return status;
}

int32_t cth_audio_open(const char *device_uid, cth_stream_config *config, cth_process_fn process, void *user, cth_stream **stream)
{
    if (!config || !process || !stream) return fail("invalid arguments", 0);
    *stream = NULL;

    AudioObjectID device = find_device(device_uid);
    if (device == kAudioObjectUnknown) return fail("audio device not found", 0);

    int32_t outputs = channel_count(device, kAudioObjectPropertyScopeOutput);
    int32_t inputs = channel_count(device, kAudioObjectPropertyScopeInput);
    if (outputs <= 0) return fail("device has no outputs", 0);
    if (outputs > MAX_CHANNELS) outputs = MAX_CHANNELS;
    if (config->input_channels < inputs) inputs = config->input_channels < 0 ? 0 : config->input_channels;
    if (inputs > MAX_CHANNELS) inputs = MAX_CHANNELS;

    OSStatus status = set_rate(device, config->sample_rate);
    if (status != noErr) return fail("could not set the sample rate", status);

    if (config->buffer_frames > 0) {
        AudioObjectPropertyAddress range_address = address(kAudioDevicePropertyBufferFrameSizeRange, kAudioObjectPropertyScopeGlobal);
        AudioValueRange range = {0, 0};
        UInt32 size = sizeof range;
        AudioObjectGetPropertyData(device, &range_address, 0, NULL, &size, &range);
        UInt32 frames = (UInt32)config->buffer_frames;
        if (range.mMaximum > 0) {
            if (frames < (UInt32)range.mMinimum) frames = (UInt32)range.mMinimum;
            if (frames > (UInt32)range.mMaximum) frames = (UInt32)range.mMaximum;
        }
        AudioObjectPropertyAddress a = address(kAudioDevicePropertyBufferFrameSize, kAudioObjectPropertyScopeGlobal);
        status = AudioObjectSetPropertyData(device, &a, 0, NULL, sizeof frames, &frames);
        if (status != noErr) return fail("could not set the buffer size", status);
    }

    cth_stream *s = (cth_stream *)calloc(1, sizeof *s);
    if (!s) return fail("out of memory", 0);
    s->device = device;
    s->process = process;
    s->user = user;
    s->input_channels = inputs;
    s->output_channels = outputs;
    s->buffer_frames = (int32_t)get_u32(device, kAudioDevicePropertyBufferFrameSize, kAudioObjectPropertyScopeGlobal);
    s->input_latency = inputs > 0 ? total_latency(device, kAudioObjectPropertyScopeInput, s->buffer_frames) : 0;
    s->output_latency = total_latency(device, kAudioObjectPropertyScopeOutput, s->buffer_frames);
    s->planar = (float *)calloc((size_t)(inputs + outputs) * CTH_MAX_FRAMES, sizeof(float));
    if (!s->planar) {
        free(s);
        return fail("out of memory", 0);
    }
    for (int32_t c = 0; c < inputs; c++) s->in_ptrs[c] = s->planar + (size_t)c * CTH_MAX_FRAMES;
    for (int32_t c = 0; c < outputs; c++) s->out_ptrs[c] = s->planar + (size_t)(inputs + c) * CTH_MAX_FRAMES;

    status = AudioDeviceCreateIOProcID(device, io_proc, s, &s->proc_id);
    if (status != noErr) {
        free(s->planar);
        free(s);
        return fail("could not create the IOProc", status);
    }

    config->sample_rate = nominal_rate(device);
    s->sample_rate = config->sample_rate;
    s->follows_default = !device_uid || !device_uid[0];
    add_listeners(s);

    mach_timebase_info_data_t timebase;
    mach_timebase_info(&timebase);
    s->ticks_to_ns = (double)timebase.numer / (double)timebase.denom;
    config->buffer_frames = s->buffer_frames;
    config->input_channels = inputs;
    config->output_channels = outputs;
    *stream = s;
    return 0;
}

int32_t cth_audio_start(cth_stream *s)
{
    if (!s) return fail("no stream", 0);
    OSStatus status = AudioDeviceStart(s->device, s->proc_id);
    if (status != noErr) return fail("could not start the device", status);
    s->running = 1;
    return 0;
}

int32_t cth_audio_stop(cth_stream *s)
{
    if (!s || !s->running) return 0;
    OSStatus status = AudioDeviceStop(s->device, s->proc_id);
    s->running = 0;
    return status == noErr ? 0 : fail("could not stop the device", status);
}

void cth_audio_close(cth_stream *s)
{
    if (!s) return;
    cth_audio_stop(s);
    remove_listeners(s);
    AudioDeviceDestroyIOProcID(s->device, s->proc_id);
    free(s->planar);
    free(s);
}

void cth_audio_stats(const cth_stream *s, cth_stream_stats *stats)
{
    if (!stats) return;
    memset(stats, 0, sizeof *stats);
    if (!s) return;
    stats->callbacks = atomic_load_explicit(&((cth_stream *)s)->callbacks, memory_order_relaxed);
    stats->overloads = atomic_load_explicit(&((cth_stream *)s)->overloads, memory_order_relaxed);
    stats->input_latency_frames = s->input_latency;
    stats->output_latency_frames = s->output_latency;
}

void cth_audio_timing(const cth_stream *s, cth_stream_timing *timing)
{
    if (!timing) return;
    if (!s) {
        memset(timing, 0, sizeof *timing);
        return;
    }
    memcpy(timing, &s->timing, sizeof *timing); /* racy snapshot of counters; fine for statistics */
}

void cth_audio_reset_timing(cth_stream *s)
{
    if (s) atomic_store_explicit(&s->reset_timing, 1, memory_order_release);
}

int32_t cth_audio_status(const cth_stream *s)
{
    return s ? atomic_load_explicit(&((cth_stream *)s)->status, memory_order_relaxed) : CTH_STREAM_OK;
}
