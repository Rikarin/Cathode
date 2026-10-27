This chapter covers everything between your guitar cable and your speakers: choosing the audio interface, the sample rate and buffer size, latency and how to measure it, routing inputs and outputs to the interface's channels, setting and calibrating the input level, the outputs' own settings, and what to plug Cathode into.

You find almost all of it in **Settings ▸ Audio setup** (press **⌘,**, then click **Audio setup**). The per-output settings live on the **Global** page.

## The audio interface

An audio interface converts your guitar's electrical signal into digital samples, hands them to the computer, and converts the processed samples back into a signal for your speakers. A good one for guitar has:

- an **instrument input** (Hi-Z), so a passive guitar pickup is not loaded down and dulled;
- low **round-trip latency** at small buffer sizes (its driver and USB path add a fixed delay you cannot remove);
- enough outputs for what you plan: two for headphones or a pair of monitors, more if you want separate feeds for front of house and your own monitor, a DI out for recording, or an effects loop.

Cathode opens **one device for both input and output**. Use one interface for both; it does not combine separate input and output devices.

### What Cathode uses on each system

| System  | Audio                                                                      | MIDI                 | Notes                                                                                                                                                                                                    |
| ------- | -------------------------------------------------------------------------- | -------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| macOS   | CoreAudio                                                                  | CoreMIDI             | Any class-compliant or driver-installed interface. Cathode sets the device's sample rate and buffer size itself.                                                                                         |
| Windows | WASAPI                                                                     | Windows MIDI (WinMM) | Opens the interface in exclusive mode when the device allows it (lowest latency, other apps go quiet), otherwise in shared mode. There is no ASIO support yet. Early: tested on shared-mode output only. |
| Linux   | ALSA (directly, no sound server needed) or JACK, including PipeWire's JACK | ALSA sequencer       | Early: tested in containers, not yet on a real interface.                                                                                                                                                |

If no device is chosen, or the chosen one cannot be opened, the engine runs on the **null backend**: everything works, but nothing is heard.

## The Audio setup page

The page has five parts.

**Engine** shows how the engine is doing, refreshed every two seconds:

| Figure              | What it means                                                                                                               |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Backend             | The audio system in use, "null" without a device, or "disconnected" if the engine is not running.                           |
| Sample rate, Buffer | What the engine is actually running at.                                                                                     |
| Latency (buffer)    | How long one buffer lasts, in milliseconds.                                                                                 |
| CPU p99.9, CPU max  | How much of each buffer's time the engine needed: in 999 of 1000 buffers, and at worst. Near or above 100 % means dropouts. |
| Engine overruns     | How often the engine did not finish a buffer in time.                                                                       |
| Driver overloads    | How often the audio driver reported a dropout.                                                                              |
| Inputs              | The interface inputs mapped to Cathode's input sources.                                                                     |

In the desktop app, a line under the figures shows the state of the app's two engine processes, the engine host and the Server (running, restarting after a crash, failed), and **Show engine log** shows the last lines each printed, which usually say why one stopped.

Below it, **Latency** breaks the delay down: **Device input** and **Device output** as the driver reports them (each including one buffer), **Buffer**, **Preset** (the longest path through the grid; blocks that add latency are named underneath), and the **Total**.

**Input 1** shows a warning when the input clips, and the calibration (see [below](#input-level-and-calibration)).

**Audio interface** chooses the device, rate, buffer and channel routing, and runs the latency test. **Server** chooses whether this app uses its own engine or a remote one (see [The headless server](/manual/headless-server)). **Other devices** lets a tablet or another computer edit this app's engine over the network (see [below](#control-from-other-devices)). With a remote one, the rig's interface is set on the rig, so **Audio interface** says so instead of showing its settings.

> **Note:** In a web browser (a headless rig), this page shows the engine figures and the calibration, but not the interface settings. On a rig, the interface is chosen in its settings file instead.

## Choosing the device

1. Open **Settings ▸ Audio setup**.
2. Pick your interface in **Device**. The list shows each device with its number of inputs and outputs. **No audio (null backend)** turns audio off.
3. Pick a **Sample rate** and **Buffer** (see the next sections).
4. Click **Apply and restart engine**. The button is only active when something has changed.

Only the engine restarts; your preset stays loaded. Changes to the channel routing below also need **Apply and restart engine**.

## Sample rate

The sample rate is how many times per second the signal is measured. Cathode offers **44.1 k**, **48 k**, **88.2 k** and **96 k**.

Use **48 k** unless you have a reason not to. It is what the amp models, the factory presets and the CPU estimates are made for. Higher rates halve the length of each buffer, but roughly double the work the computer has to do, and the amp and drive blocks already oversample internally where they need it.

## Buffer size and latency

The engine processes audio in small chunks called **buffers**. The interface fills a buffer from the input, Cathode processes it, and the interface plays it. A smaller buffer means less delay between picking a note and hearing it, but gives the computer less time to finish each chunk. If it runs out of time you hear a click or crackle, a **dropout**.

Cathode offers buffers of **32**, **64**, **128**, **256** and **512** samples. Aim for **64**.

| Buffer | One buffer at 48 kHz | One buffer at 96 kHz |
| ------ | -------------------- | -------------------- |
| 32     | 0.67 ms              | 0.33 ms              |
| 64     | 1.33 ms              | 0.67 ms              |
| 128    | 2.67 ms              | 1.33 ms              |
| 256    | 5.33 ms              | 2.67 ms              |
| 512    | 10.67 ms             | 5.33 ms              |

The total delay you feel, the **round trip**, is at least two buffers (one in, one out) plus the interface's own converter and USB delay, which is often several milliseconds on its own. Some blocks add latency too, and the Latency report names them. For reference, sound travels about 1 metre in 3 ms, so 6 ms is like standing 2 metres from your amp.

How to choose:

1. Start at **64**.
2. Play your heaviest preset for a few minutes. Watch **Engine overruns** and **Driver overloads** on this page.
3. If either count rises, or you hear crackles, go up one step (128) and try again.
4. If everything is clean and you want less delay, try **32**.

> **Tip:** The **Est.** figure in the navigation bar estimates how much of the computer's budget the current preset needs. Several high-gain amps, NAM captures, pitch shifters and long reverbs add up. If a preset only crackles when certain blocks are on, the preset is too heavy for that buffer size.

### Measuring the real latency

The figures above are what the driver reports. You can measure the real round trip with a cable:

1. Turn your speakers and headphones down: the test plays loud clicks.
2. Connect a cable from the interface's **output 1** to its **input 1**.
3. Click **Measure latency…**. The engine stops for a few seconds, plays clicks out of output 1, listens for them on input 1, and starts again.
4. The result shows the round trip in milliseconds, with the details of the measurement.

Remove the loopback cable afterwards. The test needs a real device: it does not run on the null backend or with a remote server.

## Routing inputs and outputs

By default Cathode reads **Input 1** from the interface's channel 1 and plays **Output 1** on channels 1 and 2. You change this in the fields under **Audio interface**. Channels are numbered from 1, as printed on the interface.

**Outputs 1-4 (first channel each)** takes up to four numbers separated by commas: the first device channel of each output's stereo pair, or 0 to leave an output unused. For example `1,3,0,0` plays Output 1 on channels 1 and 2 and Output 2 on channels 3 and 4. Output 1 is the main output. Outputs 2 to 4 play whatever reaches an **Output 2**, **Output 3** or **Output 4** block on the grid.

**DI out** sends the dry guitar, before any processing and before Input 1 Gain, to one or two device channels, for example `5,6`. Record it next to the processed sound and you can re-amp it later.

**FX loop send** and **FX loop return** are the device channels the [FX Loop block](/manual/blocks/fxloop) sends to and returns from, for example `3,4`. One channel makes the loop mono.

**Input map** has a field for each of Cathode's six input sources: **Input 1** to **Input 4**, **USB 5/6** and **USB 7/8**. Type the device channel for each: `1` for a mono source, `3,4` for a stereo pair, or leave it empty for none. Hover over a field to see the names your interface gives its channels. Input 1 feeds the grid's Input block. Blocks with an input choice (a second Input block, the Multiplexer, a compressor's sidechain, the Vocoder) can pick the others, for example a second instrument or a microphone.

Click **Apply and restart engine** after any change.

## Input level and calibration

Two things matter at the input: that it never clips, and that Cathode knows how many volts your guitar puts out.

### Avoid clipping

Play as hard as you ever will and watch the **IN** meter in the navigation bar. If its label turns into a red **CLIP** mark (the input came within 0.5 dB of full scale), turn the interface's gain knob down. Audio setup shows the same warning as **Input 1 is clipping**. Clipping at the input is digital distortion that no setting later in the chain can remove.

### Why calibrate

A real amp responds to the voltage your pickups produce: a hot humbucker drives it harder than a vintage single coil. Cathode's Amp and Drive blocks work the same way, in volts. To get volts from the interface's numbers, Cathode needs to know how many volts at the input jack reach the top of the interface's range, its **full scale**.

The default is **1 V** peak. If your interface's real full scale is higher (many instrument inputs take several volts at their lowest gain), the amps hear your guitar quieter than it is. If you have turned the gain up a lot, they hear it hotter. Calibrating fixes this, and then turning the interface's gain knob no longer changes your tone, only the headroom.

The page shows the current calibration, as **Full scale** in volts peak and in dBu.

### Calibrate from the interface's specification

1. Find your interface's **maximum input level** for the instrument input in its manual or specifications. It is usually given in dBu, and only applies at a particular gain setting, usually the lowest.
2. Set the interface's gain knob to that setting (for most, fully down).
3. In **Interface maximum input level**, type the figure, choose **dBu** or **V peak**, and click **Use this level**.

From then on, leave the gain knob where it is. If you move it, calibrate again.

### Calibrate by measuring

If you do not know the maximum level, measure it with a steady signal of known level:

1. Connect a sine-wave source of known voltage to input 1: a signal generator, or a test tone whose level you checked with a multimeter.
2. Type its level in volts RMS in **Or play a reference sine of (V RMS)**.
3. Start the tone and click **Measure for 2 s**.

Cathode reads the input's peak for two seconds and works out the full scale. If the tone clips the input, it asks you to turn it or the gain down and measure again.

### Input 1 Gain

**Global ▸ Input and oversampling ▸ Input 1 Gain** (−20 to +20 dB) trims what every preset receives, after the DI out. Use it to match a guitar that is much hotter or quieter than usual without touching your presets. Each preset's Input block also has its own **Input Level**.

## Output levels and the global output settings

Watch the **OUT** meter in the navigation bar. If its label turns into **CLIP**, an output reached −1 dBFS or above; the tooltip names which one. Lower the preset's level or that output's Boost/Pad. [Gain staging and levels](/manual/gain-staging) explains how to keep levels healthy through the chain and how to match presets.

On the **Global** page, under **Outputs**, each of Outputs 1 to 4 has settings that apply to every preset:

- **Mode:** **Stereo**; **Sum L+R** (both sides carry the mono sum, for a single speaker or a mono PA channel); **Copy L→R** (the left side on both; earlier versions also listed a Dual Mono that did the same, and settings that chose it now show Copy L→R).
- **Boost/Pad:** −18 to +18 dB, to match the output to what it feeds.
- **Global EQ:** a ten-band graphic EQ per output, to correct a room or a speaker without editing presets.
- **Copy Output 1 to 2:** in presets without an Output 2 block, Output 2 plays Output 1's signal (taken before Output 1's EQ and Boost/Pad), so each can have its own EQ and level. Useful for a front-of-house feed and a stage monitor.

The metronome plays on Output 1 only.

## What to plug into

Cathode's amp blocks model the amp, and its Cab blocks model the speaker cabinet and microphone. What you plug into decides which of those you want on.

**Headphones, studio monitors or a full-range speaker (FRFR).** The simplest and most accurate choice. Leave the Cab blocks on. You hear the sound as if through a microphone in front of a cabinet, as on a record.

**A PA or a recording interface.** The same as above: feed it from the main outputs, Cab blocks on. Use balanced cables, and set **Mode** to **Sum L+R** if the PA gives you a single mono channel.

**A guitar power amp and cabinet, or a guitar amp's effects-loop return.** The real cabinet does the cab's job, so turn Cab simulation off for everything: **Global ▸ Cab modeling ▸ Cab modeling off** makes every Cab and IR Player block pass the signal through unchanged, without changing your presets. Keep the Amp blocks, whose power-amp and speaker models still shape the feel, or try both ways.

**The front input of a guitar amp.** Possible, but rarely good: the amp's own preamp colours everything again. If you do, use Cathode for effects in front of the amp rather than for amp models, and keep Cathode's output level modest so you do not overdrive the amp's input unintentionally.

> **Warning:** A line-level output into a guitar amp's instrument input, or a power amp into anything but a speaker, can damage equipment. Check levels and connections before you turn things on, and start with every volume down.

### Stereo or mono

Many effects (stereo delays, reverbs, chorus, rotary) spread across left and right. They only sound right on two speakers. Into a single speaker, use the output's **Sum L+R** mode. Some blocks sum to mono themselves (an Amp block always does): stereo effects placed before them lose their width. The grid warns about this with a **MONO** tag; see [The grid](/manual/the-grid#stereo-and-mono).

## Control from other devices

The desktop app can also serve the editor to other devices on the same network, so you can sit on the couch with an iPad and shape the sound while the Mac plays. Both edit the same live rig at the same time: turn a knob on the iPad and it moves in the app, and the reverse.

1. Open **Settings ▸ Audio setup**.
2. Under **Other devices**, turn on **Allow control from other devices on this network**. The default port, 5180, is fine unless another program uses it; then type another port (1024 or higher) first. The app's window reloads.
3. The section now shows the addresses to open, the **access token** and a QR code.
4. On the iPad, point the Camera at the QR code and tap the link. Safari opens the editor, already signed in. Without a camera, type one of the addresses into the browser and paste the token when the editor asks for it.

The browser remembers the token, so next time the address alone is enough. In Safari, **Share ▸ Add to Home Screen** gives the editor an icon and the full screen.

The first time you turn it on, macOS may ask whether **cathode-server** may accept incoming network connections. Choose **Allow**; if you chose Deny, allow it in **System Settings ▸ Network ▸ Firewall ▸ Options**. If the iPad still cannot connect, check that both are on the same Wi-Fi (not a guest network, which often keeps devices apart) and try the address with the numbers rather than the `.local` name.

On a touch screen, double-tap an empty cell to add a block, touch and hold a block for its menu, and drag knobs up and down. Interface settings, the latency test and IR capture stay in the desktop app.

- **New token** makes a new token and signs every other device out.
- The setting stays on until you turn it off, also after a restart of the app. It is off by default, and it applies only while the app uses its own engine (not a remote one).
- Anyone on the network who has the token can change your rig, and the connection is not encrypted. Turn it on at home, not on venue or public Wi-Fi.

## Oversampling

**Global ▸ Input and oversampling ▸ Oversampling** chooses the filters the Amp, Drive and Dynamic Distortion blocks use when they oversample: **Minimum phase** adds no latency; **Linear phase** delays every frequency equally, at about 2 ms more latency. Minimum phase is the default and the right choice for playing.

## Platform notes

**macOS.** If Cathode hears nothing, check **System Settings ▸ Privacy & Security ▸ Microphone** and make sure Cathode is allowed. The latency test and the cab IR capture stop the engine briefly because the device cannot be shared while they run.

**Windows.** In exclusive mode, other applications cannot play through the same interface while Cathode runs. To keep Cathode in shared mode instead (higher latency, but other apps keep playing), set the environment variable `CTH_WASAPI_SHARED` to `1` before starting Cathode.

**Linux.** Devices appear as `alsa:<card>` for ALSA and `jack:<client>` for JACK. A desktop running PipeWire may hold the ALSA device itself; the JACK route through PipeWire's JACK support avoids that, with the rate and buffer then set by the sound server. Running the engine with real-time priority matters more on Linux than elsewhere; the [headless server](/manual/headless-server) package sets this up for you.
