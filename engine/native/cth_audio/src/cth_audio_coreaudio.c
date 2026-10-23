/*
 * CoreAudio backend: a HAL IOProc on one device. This is the lowest-latency path on macOS; the IOProc
 * thread is already real-time, so no workgroup join is needed for the callback itself.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <CoreAudio/CoreAudio.h>
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
    float *planar; /* (inputs + outputs) * CTH_MAX_FRAMES */
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

    UInt32 total = 0;
    if (output && output->mNumberBuffers > 0 && output->mBuffers[0].mNumberChannels > 0) {
        total = output->mBuffers[0].mDataByteSize / (output->mBuffers[0].mNumberChannels * sizeof(float));
    }

    for (UInt32 offset = 0; offset < total; offset += CTH_MAX_FRAMES) {
        UInt32 frames = total - offset < CTH_MAX_FRAMES ? total - offset : CTH_MAX_FRAMES;
        if (s->input_channels > 0 && input) deinterleave(input, s->in_ptrs, s->input_channels, offset, frames);
        for (int32_t c = 0; c < s->output_channels; c++) memset(s->out_ptrs[c], 0, frames * sizeof(float));
        s->process(s->user, s->in_ptrs, s->input_channels, s->out_ptrs, s->output_channels, (int32_t)frames);
        interleave(output, s->out_ptrs, s->output_channels, offset, frames);
    }

    atomic_fetch_add_explicit(&s->callbacks, 1, memory_order_relaxed);
    return noErr;
}

static OSStatus overload_listener(AudioObjectID object, UInt32 count, const AudioObjectPropertyAddress *addresses, void *client)
{
    (void)object; (void)count; (void)addresses;
    atomic_fetch_add_explicit(&((cth_stream *)client)->overloads, 1, memory_order_relaxed);
    return noErr;
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

    AudioObjectPropertyAddress overload = address(kAudioDeviceProcessorOverload, kAudioObjectPropertyScopeGlobal);
    AudioObjectAddPropertyListener(device, &overload, overload_listener, s);

    config->sample_rate = nominal_rate(device);
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
    AudioObjectPropertyAddress overload = address(kAudioDeviceProcessorOverload, kAudioObjectPropertyScopeGlobal);
    AudioObjectRemovePropertyListener(s->device, &overload, overload_listener, s);
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
