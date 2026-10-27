/*
 * ASIO backend for device uids of the form "asio:<driver key>", the key being the driver's subkey under
 * HKLM\SOFTWARE\ASIO (per-user registrations under HKCU\SOFTWARE\ASIO are listed too). Uses Steinberg's ASIO SDK
 * headers under the SDK's GPLv3 option (THIRD-PARTY-NOTICES.md).
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * ASIO allows one loaded driver per process, so the backend holds at most one: opening a second stream fails, and
 * enumeration loads each driver in turn only while none is open. Every call into a driver runs on one thread per
 * driver, a single-threaded COM apartment with a message pump, because drivers are apartment-threaded and some post
 * window messages to themselves. The driver's own thread calls bufferSwitch; the callbacks carry no user pointer, so
 * the open stream is a global. Each half-buffer is converted to planar float, handed to the process function in
 * chunks of at most CTH_MAX_FRAMES, and converted back into the driver's sample format.
 *
 * Inputs are the device's first config->input_channels channels and outputs are all of its channels (up to 64 each),
 * as on the other backends. The driver's reset, resync and buffer-size requests and a sample-rate change raise
 * CTH_STREAM_CHANGED, and the host reopens the device. Latencies are the driver's, which by the ASIO specification
 * include the buffer.
 */
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <objbase.h>
#include <process.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "asiosys.h"
#include "asio.h"
#include "iasiodrv.h"

#include "cth_asio_util.h"
#include "cth_audio_win.h"

#define MAX_CHANNELS 64
#define MAX_DRIVERS 32
#define NAME_CHARS 32 /* ASIOChannelInfo::name */

static_assert(sizeof(CLSID) == sizeof(cth_asio_guid), "cth_asio_guid must match the Windows GUID layout");

/* ---- The driver thread ------------------------------------------------------------------------------------------ */

typedef struct worker {
    HANDLE thread;
    HANDLE wake;
    HANDLE done;
    void (*job)(void *);
    void *arg;
    volatile LONG quit;
    int started;
    CRITICAL_SECTION calls; /* one job at a time */
} worker;

static unsigned __stdcall worker_main(void *arg)
{
    worker *w = (worker *)arg;
    HRESULT com = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    SetEvent(w->done);
    for (;;) {
        DWORD wait = MsgWaitForMultipleObjects(1, &w->wake, FALSE, INFINITE, QS_ALLINPUT);
        if (wait == WAIT_OBJECT_0) {
            if (w->quit) break;
            w->job(w->arg);
            SetEvent(w->done);
        } else if (wait == WAIT_OBJECT_0 + 1) {
            MSG message;
            while (PeekMessageW(&message, NULL, 0, 0, PM_REMOVE)) {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        } else {
            break;
        }
    }
    if (SUCCEEDED(com)) CoUninitialize();
    return 0;
}

static int worker_start(worker *w)
{
    InitializeCriticalSection(&w->calls);
    w->started = 1;
    w->wake = CreateEventW(NULL, FALSE, FALSE, NULL);
    w->done = CreateEventW(NULL, FALSE, FALSE, NULL);
    if (!w->wake || !w->done) return -1;
    w->thread = (HANDLE)_beginthreadex(NULL, 0, worker_main, w, 0, NULL);
    if (!w->thread) return -1;
    WaitForSingleObject(w->done, INFINITE);
    return 0;
}

/* Runs job(arg) on the driver thread and waits for it. */
static void worker_call(worker *w, void (*job)(void *), void *arg)
{
    EnterCriticalSection(&w->calls);
    w->job = job;
    w->arg = arg;
    SetEvent(w->wake);
    WaitForSingleObject(w->done, INFINITE);
    LeaveCriticalSection(&w->calls);
}

static void worker_stop(worker *w)
{
    if (w->thread) {
        InterlockedExchange(&w->quit, 1);
        SetEvent(w->wake);
        WaitForSingleObject(w->thread, INFINITE);
        CloseHandle(w->thread);
        w->thread = NULL;
    }
    if (w->wake) CloseHandle(w->wake);
    if (w->done) CloseHandle(w->done);
    w->wake = w->done = NULL;
    if (w->started) DeleteCriticalSection(&w->calls);
    w->started = 0;
}

/* ---- Drivers and the device list -------------------------------------------------------------------------------- */

typedef struct driver_entry {
    cth_asio_driver driver;
    cth_device_info info;
    int present; /* loaded when last probed, or not probed because another driver was open */
    char input_names[MAX_CHANNELS][NAME_CHARS];
    char output_names[MAX_CHANNELS][NAME_CHARS];
} driver_entry;

struct cth_asio_stream {
    worker thread;
    IASIO *driver;
    driver_entry entry; /* the driver as opened; also copied into the device list */
    cth_process_fn process;
    void *user;
    int32_t input_channels;
    int32_t output_channels;
    int32_t buffer_frames;
    double sample_rate;
    ASIOBufferInfo buffers[2 * MAX_CHANNELS]; /* inputs, then outputs */
    ASIOSampleType types[2 * MAX_CHANNELS];
    int post_output; /* the driver wants ASIOOutputReady after each buffer */
    int buffers_created;
    volatile LONG running;
    volatile LONG input_latency;
    volatile LONG output_latency;
    volatile LONG64 callbacks;
    volatile LONG64 overloads;
    volatile LONG reset_timing;      /* set by the control thread, applied by the callback */
    volatile LONG status;            /* CTH_STREAM_* */
    volatile LONG latencies_changed; /* kAsioLatenciesChanged: re-read on the next stats call */
    double ticks_to_ns;
    DWORD callback_thread; /* the thread FTZ was last enabled on; written only by the callback */
    cth_stream_timing timing; /* written only by the callback */
    float *planar;            /* (inputs + outputs) * CTH_MAX_FRAMES */
    const float *in_ptrs[MAX_CHANNELS];
    float *out_ptrs[MAX_CHANNELS];
    /* The current driver-thread job's arguments and outcome. */
    cth_stream_config *config;
    int32_t result;
    char error[256];
};

static SRWLOCK lock = SRWLOCK_INIT; /* the device list, and the one-driver rule */
static driver_entry *drivers;       /* MAX_DRIVERS entries, allocated on first enumeration */
static int32_t driver_count;
static int listed;
static cth_asio_stream *open_stream;        /* the stream holding the process's driver, under lock */
static cth_asio_stream *volatile live;      /* the same, for the driver's callbacks */

static int32_t fail(const char *message)
{
    cth_win_set_error(message);
    return -1;
}

static void to_utf8(const wchar_t *text, char *out, int size)
{
    out[0] = 0;
    if (WideCharToMultiByte(CP_UTF8, 0, text, -1, out, size, NULL, NULL) == 0) out[size - 1] = 0;
}

static void read_string(HKEY key, const wchar_t *name, char *out, int size)
{
    wchar_t wide[256];
    DWORD bytes = sizeof wide;
    out[0] = 0;
    if (RegGetValueW(key, NULL, name, RRF_RT_REG_SZ, NULL, wide, &bytes) == ERROR_SUCCESS) to_utf8(wide, out, size);
}

/* The installed drivers, machine-wide first; a per-user key with the same name as a machine one is ignored. */
static int32_t list_drivers(cth_asio_driver *out, int32_t max)
{
    int32_t n = 0;
    const HKEY roots[] = {HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER};
    for (HKEY root : roots) {
        HKEY asio;
        if (RegOpenKeyExW(root, L"SOFTWARE\\ASIO", 0, KEY_READ, &asio) != ERROR_SUCCESS) continue;
        for (DWORD i = 0; n < max; i++) {
            wchar_t name[128];
            DWORD length = 128;
            LONG rc = RegEnumKeyExW(asio, i, name, &length, NULL, NULL, NULL, NULL);
            if (rc == ERROR_NO_MORE_ITEMS) break;
            if (rc != ERROR_SUCCESS) continue;
            HKEY sub;
            if (RegOpenKeyExW(asio, name, 0, KEY_READ, &sub) != ERROR_SUCCESS) continue;
            char key[128], clsid[64], description[256];
            to_utf8(name, key, sizeof key);
            read_string(sub, L"CLSID", clsid, sizeof clsid);
            read_string(sub, L"Description", description, sizeof description);
            RegCloseKey(sub);
            int duplicate = 0;
            for (int32_t d = 0; d < n && !duplicate; d++) duplicate = strcmp(out[d].key, key) == 0;
            if (!duplicate && cth_asio_driver_entry(key, clsid, description, &out[n]) == 0) n++;
        }
        RegCloseKey(asio);
    }
    return n;
}

/* Creates and initialises a driver on the calling (driver) thread; NULL with error filled on failure. */
static IASIO *load(const cth_asio_driver *d, char *error, size_t size)
{
    CLSID clsid;
    memcpy(&clsid, &d->clsid, sizeof clsid);
    IASIO *driver = NULL;
    /* ASIO drivers use their class id as the interface id too. */
    HRESULT hr = CoCreateInstance(clsid, NULL, CLSCTX_INPROC_SERVER, clsid, (void **)&driver);
    if (FAILED(hr) || !driver) {
        snprintf(error, size, "could not load the ASIO driver \"%s\" (HRESULT 0x%08lX)", d->name, (unsigned long)hr);
        return NULL;
    }
    if (!driver->init(GetDesktopWindow())) {
        char message[128] = ""; /* the specification guarantees 124 bytes */
        driver->getErrorMessage(message);
        message[sizeof message - 1] = 0;
        snprintf(error, size, "the ASIO driver \"%s\" did not start%s%s (is the device connected?)", d->name,
                 message[0] ? ": " : "", message);
        driver->Release();
        return NULL;
    }
    return driver;
}

/* Fills an entry's channels, rate and channel names from a loaded driver. */
static void describe(IASIO *driver, driver_entry *e)
{
    long inputs = 0, outputs = 0;
    ASIOSampleRate rate = 0;
    driver->getChannels(&inputs, &outputs);
    driver->getSampleRate(&rate);
    snprintf(e->info.uid, sizeof e->info.uid, "%s", e->driver.uid);
    snprintf(e->info.name, sizeof e->info.name, "%s", e->driver.name);
    e->info.input_channels = inputs < 0 ? 0 : inputs > MAX_CHANNELS ? MAX_CHANNELS : (int32_t)inputs;
    e->info.output_channels = outputs < 0 ? 0 : outputs > MAX_CHANNELS ? MAX_CHANNELS : (int32_t)outputs;
    e->info.sample_rate = rate;
    for (int side = 0; side < 2; side++) {
        int32_t count = side ? e->info.input_channels : e->info.output_channels;
        for (int32_t c = 0; c < count; c++) {
            ASIOChannelInfo channel;
            memset(&channel, 0, sizeof channel);
            channel.channel = c;
            channel.isInput = side ? ASIOTrue : ASIOFalse;
            char *name = side ? e->input_names[c] : e->output_names[c];
            name[0] = 0;
            if (driver->getChannelInfo(&channel) == ASE_OK) {
                memcpy(name, channel.name, NAME_CHARS);
                name[NAME_CHARS - 1] = 0;
            }
        }
    }
    e->present = 1;
}

typedef struct probe_job {
    driver_entry *entries;
    int32_t count;
} probe_job;

/* Loads each driver in turn, on the driver thread. One that does not load (its device unplugged) stays absent. */
static void probe(void *arg)
{
    probe_job *job = (probe_job *)arg;
    for (int32_t i = 0; i < job->count; i++) {
        char error[256];
        IASIO *driver = load(&job->entries[i].driver, error, sizeof error);
        if (!driver) continue;
        describe(driver, &job->entries[i]);
        driver->Release();
    }
}

int32_t cth_asio_device_count(void)
{
    cth_asio_driver found[MAX_DRIVERS];
    int32_t n = list_drivers(found, MAX_DRIVERS);
    driver_entry *next = (driver_entry *)calloc(MAX_DRIVERS, sizeof *next);
    if (!next) return 0;
    for (int32_t i = 0; i < n; i++) {
        next[i].driver = found[i];
        snprintf(next[i].info.uid, sizeof next[i].info.uid, "%s", found[i].uid);
        snprintf(next[i].info.name, sizeof next[i].info.name, "%s", found[i].name);
    }

    AcquireSRWLockExclusive(&lock);
    if (open_stream) {
        /* A driver is loaded, so no other may be: report each as last seen (the open one as opened). */
        for (int32_t i = 0; i < n; i++) {
            next[i].present = 1;
            if (strcmp(found[i].uid, open_stream->entry.driver.uid) == 0) {
                next[i] = open_stream->entry;
                continue;
            }
            for (int32_t p = 0; drivers && p < driver_count; p++) {
                if (strcmp(drivers[p].driver.uid, found[i].uid) == 0) next[i] = drivers[p];
            }
        }
    } else if (n > 0) {
        worker thread;
        memset(&thread, 0, sizeof thread);
        probe_job job = {next, n};
        if (worker_start(&thread) == 0) worker_call(&thread, probe, &job);
        worker_stop(&thread);
    }

    if (!drivers) drivers = (driver_entry *)calloc(MAX_DRIVERS, sizeof *drivers);
    driver_count = 0;
    for (int32_t i = 0; drivers && i < n; i++) {
        if (next[i].present) drivers[driver_count++] = next[i];
    }
    listed = 1;
    int32_t count = driver_count;
    ReleaseSRWLockExclusive(&lock);
    free(next);
    return count;
}

/* Copies list entry index (building the list on first use). 0 on success. */
static int32_t entry_at(int32_t index, driver_entry *out)
{
    AcquireSRWLockShared(&lock);
    int need_list = !listed;
    ReleaseSRWLockShared(&lock);
    if (need_list) cth_asio_device_count();
    AcquireSRWLockShared(&lock);
    int ok = index >= 0 && index < driver_count;
    if (ok) *out = drivers[index];
    ReleaseSRWLockShared(&lock);
    return ok ? 0 : fail("device index out of range");
}

int32_t cth_asio_device_info(int32_t index, cth_device_info *info)
{
    driver_entry *e = (driver_entry *)malloc(sizeof *e);
    if (!e) return fail("out of memory");
    int32_t result = entry_at(index, e);
    if (result == 0) *info = e->info;
    free(e);
    return result;
}

int32_t cth_asio_channel_name(int32_t index, int32_t is_input, int32_t channel, char *name, int32_t size)
{
    driver_entry *e = (driver_entry *)malloc(sizeof *e);
    if (!e) return fail("out of memory");
    int32_t result = entry_at(index, e);
    if (result == 0) {
        if (channel < 0 || channel >= (is_input ? e->info.input_channels : e->info.output_channels)) {
            result = fail("channel out of range");
        } else {
            snprintf(name, (size_t)size, "%s", is_input ? e->input_names[channel] : e->output_names[channel]);
        }
    }
    free(e);
    return result;
}

/* ---- Callbacks (the driver's threads) --------------------------------------------------------------------------- */

static void raise_status(cth_asio_stream *s, LONG value)
{
    for (;;) {
        LONG old = s->status;
        if (old >= value || InterlockedCompareExchange(&s->status, value, old) == old) return;
    }
}

static void record_timing(cth_asio_stream *s, LONGLONG started)
{
    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    double elapsed_ns = (double)(now.QuadPart - started) * s->ticks_to_ns;
    double percent = elapsed_ns / (s->buffer_frames / s->sample_rate * 1e9) * 100.0;
    int bin = percent >= CTH_TIMING_BINS - 1 ? CTH_TIMING_BINS - 1 : (int)percent;
    s->timing.bins[bin]++;
    if (percent > s->timing.max_percent) s->timing.max_percent = percent;
    if (percent >= 100.0) s->timing.over_budget++;
}

static void buffer_switch(long index, ASIOBool direct)
{
    (void)direct;
    cth_asio_stream *s = live;
    if (!s || !s->running) return;
    index &= 1;
    LARGE_INTEGER started;
    QueryPerformanceCounter(&started);
    DWORD thread = GetCurrentThreadId();
    if (thread != s->callback_thread) {
        cth_fpu_flush_denormals();
        s->callback_thread = thread;
    }
    if (InterlockedExchange(&s->reset_timing, 0)) memset(&s->timing, 0, sizeof s->timing);

    int32_t inputs = s->input_channels;
    for (int32_t offset = 0; offset < s->buffer_frames;) {
        int32_t chunk = s->buffer_frames - offset;
        if (chunk > CTH_MAX_FRAMES) chunk = CTH_MAX_FRAMES;
        for (int32_t c = 0; c < inputs; c++)
            cth_asio_to_float(s->types[c], s->buffers[c].buffers[index], offset, (float *)s->in_ptrs[c], chunk);
        for (int32_t c = 0; c < s->output_channels; c++) memset(s->out_ptrs[c], 0, (size_t)chunk * sizeof(float));
        s->process(s->user, s->in_ptrs, inputs, s->out_ptrs, s->output_channels, chunk);
        for (int32_t c = 0; c < s->output_channels; c++)
            cth_asio_from_float(s->types[inputs + c], s->out_ptrs[c], s->buffers[inputs + c].buffers[index], offset,
                                chunk);
        offset += chunk;
    }
    if (s->post_output) s->driver->outputReady();
    InterlockedIncrement64(&s->callbacks);
    record_timing(s, started.QuadPart);
}

static ASIOTime *buffer_switch_time_info(ASIOTime *params, long index, ASIOBool direct)
{
    buffer_switch(index, direct);
    return params;
}

static void sample_rate_changed(ASIOSampleRate rate)
{
    (void)rate;
    cth_asio_stream *s = live;
    if (s) raise_status(s, CTH_STREAM_CHANGED);
}

static long asio_message(long selector, long value, void *message, double *opt)
{
    (void)message;
    (void)opt;
    cth_asio_stream *s = live;
    switch (selector) {
    case kAsioSelectorSupported:
        return value == kAsioEngineVersion || value == kAsioResetRequest || value == kAsioBufferSizeChange
            || value == kAsioResyncRequest || value == kAsioLatenciesChanged || value == kAsioSupportsTimeInfo
            || value == kAsioOverload;
    case kAsioEngineVersion:
        return 2;
    case kAsioResetRequest:     /* the driver must be reopened (for example after its control panel changed it) */
    case kAsioBufferSizeChange: /* the user changed the buffer size */
    case kAsioResyncRequest:    /* the driver lost sync and asks the host to restart */
        /* The host reopens the device; this callback must return into the driver first, so nothing is torn down
         * here. */
        if (s) raise_status(s, CTH_STREAM_CHANGED);
        return 1;
    case kAsioLatenciesChanged:
        if (s) InterlockedExchange(&s->latencies_changed, 1);
        return 1;
    case kAsioOverload:
        if (s) InterlockedIncrement64(&s->overloads);
        return 1;
    case kAsioSupportsTimeInfo:
        return 1;
    default:
        return 0;
    }
}

static ASIOCallbacks callbacks = {buffer_switch, sample_rate_changed, asio_message, buffer_switch_time_info};

/* ---- Stream jobs (the driver thread) ---------------------------------------------------------------------------- */

static void job_error(cth_asio_stream *s, const char *what, ASIOError error)
{
    snprintf(s->error, sizeof s->error, "the ASIO driver \"%s\" %s (ASIO error %ld)", s->entry.driver.name, what,
             (long)error);
}

static void read_latencies(cth_asio_stream *s)
{
    long input = 0, output = 0;
    if (s->driver->getLatencies(&input, &output) != ASE_OK) return;
    InterlockedExchange(&s->input_latency, s->input_channels > 0 ? input : 0);
    InterlockedExchange(&s->output_latency, output);
}

/* Both halves of every output buffer to silence, so nothing stale plays before the first callback. */
static void silence_outputs(cth_asio_stream *s)
{
    for (int32_t c = 0; c < s->output_channels; c++) {
        int32_t b = s->input_channels + c;
        size_t bytes = (size_t)s->buffer_frames * (size_t)cth_asio_sample_bytes(s->types[b]);
        for (int half = 0; half < 2; half++) {
            if (s->buffers[b].buffers[half]) memset(s->buffers[b].buffers[half], 0, bytes);
        }
    }
}

static void open_job(void *arg)
{
    cth_asio_stream *s = (cth_asio_stream *)arg;
    cth_stream_config *config = s->config;
    s->result = -1;
    s->driver = load(&s->entry.driver, s->error, sizeof s->error);
    if (!s->driver) return;
    IASIO *d = s->driver;
    describe(d, &s->entry);
    if (s->entry.info.output_channels <= 0) {
        snprintf(s->error, sizeof s->error, "the ASIO driver \"%s\" has no outputs", s->entry.driver.name);
        return;
    }

    /* Sample rate: the requested one, else the driver's (48 kHz when it reports none until told). */
    ASIOSampleRate current = 0;
    d->getSampleRate(&current);
    ASIOSampleRate rate = config->sample_rate > 0 ? config->sample_rate : current > 0 ? current : 48000.0;
    if (rate != current) {
        if (d->canSampleRate(rate) != ASE_OK) {
            snprintf(s->error, sizeof s->error, "the ASIO driver \"%s\" does not support %.0f Hz",
                     s->entry.driver.name, rate);
            return;
        }
        ASIOError error = d->setSampleRate(rate);
        if (error != ASE_OK) {
            job_error(s, "could not change its sample rate", error);
            return;
        }
    }

    long min = 0, max = 0, preferred = 0, granularity = 0;
    ASIOError error = d->getBufferSize(&min, &max, &preferred, &granularity);
    if (error != ASE_OK) {
        job_error(s, "did not report its buffer sizes", error);
        return;
    }
    int32_t frames = cth_asio_pick_buffer(min, max, preferred, granularity, config->buffer_frames);

    int32_t inputs = config->input_channels < s->entry.info.input_channels ? config->input_channels
                                                                           : s->entry.info.input_channels;
    s->input_channels = inputs < 0 ? 0 : inputs;
    s->output_channels = s->entry.info.output_channels;
    int32_t channels = s->input_channels + s->output_channels;
    for (int32_t i = 0; i < channels; i++) {
        int is_input = i < s->input_channels;
        s->buffers[i].isInput = is_input ? ASIOTrue : ASIOFalse;
        s->buffers[i].channelNum = is_input ? i : i - s->input_channels;
        s->buffers[i].buffers[0] = s->buffers[i].buffers[1] = NULL;
    }

    live = s; /* the driver may send messages while it creates the buffers */
    error = d->createBuffers(s->buffers, channels, frames, &callbacks);
    if (error != ASE_OK && frames != preferred) {
        frames = preferred; /* some drivers accept only their preferred size */
        error = d->createBuffers(s->buffers, channels, frames, &callbacks);
    }
    if (error != ASE_OK) {
        job_error(s, "could not create its buffers", error);
        return;
    }
    s->buffers_created = 1;
    s->buffer_frames = frames;

    for (int32_t i = 0; i < channels; i++) {
        ASIOChannelInfo channel;
        memset(&channel, 0, sizeof channel);
        channel.channel = s->buffers[i].channelNum;
        channel.isInput = s->buffers[i].isInput;
        error = d->getChannelInfo(&channel);
        if (error != ASE_OK) {
            job_error(s, "did not describe a channel", error);
            return;
        }
        if (cth_asio_sample_bytes(channel.type) == 0) {
            snprintf(s->error, sizeof s->error,
                     "the ASIO driver \"%s\" uses %s samples, which Cathode does not convert", s->entry.driver.name,
                     cth_asio_sample_name(channel.type));
            return;
        }
        s->types[i] = channel.type;
    }

    current = 0;
    d->getSampleRate(&current);
    s->sample_rate = current > 0 ? current : rate;
    s->entry.info.sample_rate = s->sample_rate;
    read_latencies(s);
    s->post_output = d->outputReady() == ASE_OK;
    silence_outputs(s);
    s->result = 0;
}

static void start_job(void *arg)
{
    cth_asio_stream *s = (cth_asio_stream *)arg;
    silence_outputs(s);
    InterlockedExchange(&s->running, 1);
    ASIOError error = s->driver->start();
    s->result = error == ASE_OK ? 0 : -1;
    if (error != ASE_OK) {
        InterlockedExchange(&s->running, 0);
        job_error(s, "could not start", error);
    }
}

static void stop_job(void *arg)
{
    cth_asio_stream *s = (cth_asio_stream *)arg;
    s->driver->stop();
    InterlockedExchange(&s->running, 0);
}

static void close_job(void *arg)
{
    cth_asio_stream *s = (cth_asio_stream *)arg;
    if (s->driver) {
        if (s->running) stop_job(s);
        if (s->buffers_created) s->driver->disposeBuffers();
        s->buffers_created = 0;
    }
    live = NULL;
    if (s->driver) s->driver->Release();
    s->driver = NULL;
}

static void latency_job(void *arg) { read_latencies((cth_asio_stream *)arg); }

/* ---- Public (to cth_audio_wasapi.c) ----------------------------------------------------------------------------- */

static void destroy(cth_asio_stream *s)
{
    if (s->thread.thread) worker_call(&s->thread, close_job, s);
    worker_stop(&s->thread);
    free(s->planar);
    free(s);
}

int32_t cth_asio_open(const char *uid, cth_stream_config *config, cth_process_fn process, void *user,
                      cth_asio_stream **stream)
{
    const char *key = cth_asio_uid_key(uid);
    if (!key || !config || !process || !stream) return fail("invalid arguments");
    *stream = NULL;

    cth_asio_driver found[MAX_DRIVERS];
    int32_t n = list_drivers(found, MAX_DRIVERS);
    const cth_asio_driver *driver = NULL;
    for (int32_t i = 0; i < n && !driver; i++) {
        if (strcmp(found[i].key, key) == 0) driver = &found[i];
    }
    if (!driver) {
        char message[256];
        snprintf(message, sizeof message, "ASIO driver \"%s\" is not installed", key);
        return fail(message);
    }

    cth_asio_stream *s = (cth_asio_stream *)calloc(1, sizeof *s);
    if (!s) return fail("out of memory");
    s->entry.driver = *driver;
    s->process = process;
    s->user = user;
    s->config = config;
    LARGE_INTEGER frequency;
    QueryPerformanceFrequency(&frequency);
    s->ticks_to_ns = 1e9 / (double)frequency.QuadPart;

    AcquireSRWLockExclusive(&lock);
    if (open_stream) {
        char message[256];
        snprintf(message, sizeof message,
                 "the ASIO driver \"%s\" is already open in this process, and ASIO allows one driver at a time",
                 open_stream->entry.driver.name);
        ReleaseSRWLockExclusive(&lock);
        free(s);
        return fail(message);
    }

    if (worker_start(&s->thread) < 0) {
        snprintf(s->error, sizeof s->error, "could not create the ASIO driver thread");
    } else {
        worker_call(&s->thread, open_job, s);
        if (s->result == 0) {
            s->planar = (float *)calloc((size_t)(s->input_channels + s->output_channels) * CTH_MAX_FRAMES,
                                        sizeof(float));
            if (!s->planar) {
                s->result = -1;
                snprintf(s->error, sizeof s->error, "out of memory");
            }
        }
    }
    if (s->result != 0 || !s->thread.thread) {
        char message[256];
        snprintf(message, sizeof message, "%s", s->error[0] ? s->error : "could not open the ASIO driver");
        destroy(s);
        ReleaseSRWLockExclusive(&lock);
        return fail(message);
    }

    for (int32_t c = 0; c < s->input_channels; c++) s->in_ptrs[c] = s->planar + (size_t)c * CTH_MAX_FRAMES;
    for (int32_t c = 0; c < s->output_channels; c++)
        s->out_ptrs[c] = s->planar + (size_t)(s->input_channels + c) * CTH_MAX_FRAMES;
    open_stream = s;
    for (int32_t i = 0; drivers && i < driver_count; i++) {
        if (strcmp(drivers[i].driver.uid, s->entry.driver.uid) == 0) drivers[i] = s->entry;
    }
    ReleaseSRWLockExclusive(&lock);

    config->sample_rate = s->sample_rate;
    config->buffer_frames = s->buffer_frames;
    config->input_channels = s->input_channels;
    config->output_channels = s->output_channels;
    *stream = s;
    return 0;
}

int32_t cth_asio_start(cth_asio_stream *s)
{
    if (s->running) return 0;
    worker_call(&s->thread, start_job, s);
    return s->result == 0 ? 0 : fail(s->error);
}

int32_t cth_asio_stop(cth_asio_stream *s)
{
    if (s->running) worker_call(&s->thread, stop_job, s);
    return 0;
}

void cth_asio_close(cth_asio_stream *s)
{
    AcquireSRWLockExclusive(&lock);
    destroy(s);
    open_stream = NULL;
    ReleaseSRWLockExclusive(&lock);
}

void cth_asio_stats(cth_asio_stream *s, cth_stream_stats *stats)
{
    if (InterlockedExchange(&s->latencies_changed, 0)) worker_call(&s->thread, latency_job, s);
    stats->callbacks = (uint64_t)InterlockedCompareExchange64(&s->callbacks, 0, 0);
    stats->overloads = (uint64_t)InterlockedCompareExchange64(&s->overloads, 0, 0);
    stats->input_latency_frames = InterlockedCompareExchange(&s->input_latency, 0, 0);
    stats->output_latency_frames = InterlockedCompareExchange(&s->output_latency, 0, 0);
}

void cth_asio_timing(const cth_asio_stream *s, cth_stream_timing *timing)
{
    memcpy(timing, &s->timing, sizeof *timing); /* racy snapshot of counters; fine for statistics */
}

void cth_asio_reset_timing(cth_asio_stream *s) { InterlockedExchange(&s->reset_timing, 1); }

int32_t cth_asio_status(const cth_asio_stream *s)
{
    return (int32_t)InterlockedCompareExchange((volatile LONG *)&s->status, 0, 0);
}
