import { type ChildProcess, execFile, spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { readFile, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { type Command, engine } from './engine';
import { portFree } from './network';
import type { AudioSettings, NetworkSettings } from './settings';

export interface SidecarStatus {
  name: 'host' | 'server';
  state: 'starting' | 'running' | 'restarting' | 'stopped' | 'failed';
  detail: string | null;
}

export interface IrCaptureOptions {
  seconds: number;
  levelDb: number;
  lengthMs: number;
}

export interface IrCaptureResult {
  ok: boolean;
  output: string;
  /** The captured IR as a WAV file, for the renderer to upload as an asset. */
  wav: Uint8Array | null;
}

export interface Connection {
  baseUrl: string;
  token: string;
}

const MaxBackoffMs = 10_000;
const ReadyTimeoutMs = 60_000;
/** The host's GC budget (docs/adr/0001-process-model.md): NativeAOT reads it from the environment only, as hex. */
const Gen0Budget = '10000000';

/**
 * One supervised child process: started with a command line computed at each (re)start, restarted with backoff
 * when it exits on its own, stopped politely (SIGTERM, then SIGKILL after 3 s) on request.
 */
class Sidecar {
  private child: ChildProcess | null = null;
  private wanted = false;
  private failures = 0;
  private restartTimer: NodeJS.Timeout | null = null;
  readonly log: string[] = [];
  status: SidecarStatus;

  constructor(
    readonly name: 'host' | 'server',
    private readonly commandLine: () => Command & { env?: NodeJS.ProcessEnv },
    private readonly onStatus: () => void
  ) {
    this.status = { name, state: 'stopped', detail: null };
  }

  /** Starts the process unless it already runs (or is about to restart): never a second one beside the first. */
  start(): void {
    this.wanted = true;
    if (this.child || this.restartTimer) {
      return;
    }

    this.launch();
  }

  /** Whether the process runs now. */
  get running(): boolean {
    return this.child !== null;
  }

  async stop(): Promise<void> {
    this.wanted = false;
    if (this.restartTimer) {
      clearTimeout(this.restartTimer);
      this.restartTimer = null;
    }

    const child = this.child;
    if (child && child.exitCode === null) {
      const exited = new Promise<void>(done => child.once('exit', () => done()));
      child.kill('SIGTERM');
      const killer = setTimeout(() => child.kill('SIGKILL'), 3_000);
      await exited;
      clearTimeout(killer);
    }

    this.set('stopped', null);
  }

  /** Called once the process proves healthy (the server answered), so the next failure starts the backoff over. */
  healthy(): void {
    this.failures = 0;
    this.set('running', null);
  }

  private launch(): void {
    this.restartTimer = null;
    let command: ReturnType<typeof this.commandLine>;
    try {
      command = this.commandLine();
    } catch (error) {
      this.set('failed', (error as Error).message);
      return;
    }

    this.set(this.failures === 0 ? 'starting' : 'restarting', null);
    const child = spawn(command.command, command.args, {
      env: { ...process.env, ...command.env },
      // Standard input is the child's lifeline (StdinWatch in the engine): the OS closes it however this app ends, a
      // crash or a force quit included, and the child then stops on its own instead of outliving the app.
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true
    });
    this.child = child;
    child.stdin?.on('error', () => undefined); // a child that already exited: nothing to tell it
    const keep = (data: Buffer) => {
      this.log.push(...data.toString().split('\n').filter(Boolean));
      this.log.splice(0, Math.max(0, this.log.length - 200));
    };
    child.stdout?.on('data', keep);
    child.stderr?.on('data', keep);
    const ended = (code: number | string | null) => {
      if (this.child !== child) {
        return;
      }

      this.child = null;
      if (!this.wanted) {
        return;
      }

      const delay = Math.min(MaxBackoffMs, 500 * 2 ** this.failures++);
      this.set('restarting', `exited (${code}); restarting in ${Math.round(delay / 1000)} s`);
      this.restartTimer = setTimeout(() => this.launch(), delay);
    };
    child.once('exit', (code, signal) => ended(signal ?? code));
    // A process that never started emits no exit: it ends here, so it is retried like one that crashed.
    child.once('error', error => {
      this.log.push(`spawn failed: ${error.message}`);
      if (child.pid === undefined) {
        ended(error.message);
      }
    });
  }

  private set(state: SidecarStatus['state'], detail: string | null): void {
    this.status = { name: this.name, state, detail };
    this.onStatus();
  }
}

/**
 * The local engine: cathode-host on the chosen interface and cathode-server in front of it, on loopback, and with
 * control from other devices on, also on every interface at the network port, serving the web editor there.
 */
export class Sidecars {
  private readonly socket = join(tmpdir(), `cathode-${process.pid}.sock`);
  /** A token for this run only, while the Server is on loopback alone. */
  private readonly runToken = randomBytes(32).toString('base64url');
  private port = 0;
  private ready: Promise<Connection> | null = null;
  /** The host's exclusive jobs (restart, latency test, IR capture), one at a time: each stops and restarts it. */
  private hostJobs: Promise<unknown> = Promise.resolve();
  private readonly host: Sidecar;
  private readonly server: Sidecar;
  /** Why the Server is not on the network although the setting asks for it, or null. */
  networkError: string | null = null;

  constructor(
    private audio: AudioSettings,
    private network: NetworkSettings,
    private readonly editorDirectory: string,
    private readonly dataDirectory: string | null,
    onStatus: (statuses: SidecarStatus[]) => void
  ) {
    const notify = () => onStatus(this.statuses());
    this.host = new Sidecar('host', () => this.hostCommand(), notify);
    this.server = new Sidecar('server', () => this.serverCommand(), notify);
  }

  statuses(): SidecarStatus[] {
    return [this.host.status, this.server.status];
  }

  logs(): Record<'host' | 'server', string[]> {
    return { host: this.host.log, server: this.server.log };
  }

  /** Starts both processes; resolves with the connection once the Server answers. */
  start(): Promise<Connection> {
    this.ready ??= (async () => {
      // A retry after a slow first start finds the Server still starting: it keeps its port and is not started twice.
      if (this.port === 0 || !this.server.running) {
        this.port = await freePort();
      }

      this.host.start();
      const connection = await this.serve();
      this.host.healthy();
      return connection;
    })().catch(error => {
      this.ready = null;
      throw error;
    });
    return this.ready;
  }

  /**
   * Restarts only the Server with new network settings; the host keeps playing. Resolves with the new connection
   * (its token changes when the network does).
   */
  async applyNetwork(network: NetworkSettings): Promise<Connection> {
    await this.start();
    this.network = network;
    await this.server.stop();
    this.ready = this.serve().catch(error => {
      this.ready = null;
      throw error;
    });
    return this.ready;
  }

  /** Starts the Server and waits for it. A network port in use leaves it on loopback, with the reason kept. */
  private async serve(): Promise<Connection> {
    const wanted = this.network.enabled && this.network.token.length > 0;
    this.networkError =
      wanted && !(await portFree(this.network.port))
        ? `Port ${this.network.port} is in use by another program. Choose another port.`
        : null;
    this.server.start();
    const connection = { baseUrl: `http://127.0.0.1:${this.port}`, token: this.token() };
    await waitForServer(connection);
    this.server.healthy();
    return connection;
  }

  /** Whether the Server listens on the network: the setting is on and its port was free. */
  private onNetwork(): boolean {
    return this.network.enabled && this.network.token.length > 0 && this.networkError === null;
  }

  /** The network's token while the Server is on the network (both editors use it), else this run's. */
  private token(): string {
    return this.onNetwork() ? this.network.token : this.runToken;
  }

  async stop(): Promise<void> {
    await Promise.all([this.server.stop(), this.host.stop()]);
    this.ready = null;
  }

  /** Restarts only the engine host with new audio settings; the Server reconnects and resends the rig. */
  applyAudio(audio: AudioSettings): Promise<void> {
    return this.exclusive(async () => {
      this.audio = audio;
      await this.host.stop();
      this.host.start();
      this.host.healthy();
    });
  }

  /** Runs `job` after the host's other jobs: two of them overlapping would each restart the host, starting two. */
  private exclusive<T>(job: () => Promise<T>): Promise<T> {
    const run = this.hostJobs.then(job, job);
    this.hostJobs = run.catch(() => undefined);
    return run;
  }

  /** Runs the host's loopback latency test with the engine stopped (the device cannot be shared), then restarts it. */
  latencyTest(): Promise<{ ok: boolean; output: string; roundTripMs: number | null }> {
    return this.exclusive(() => this.runLatencyTest());
  }

  private async runLatencyTest(): Promise<{ ok: boolean; output: string; roundTripMs: number | null }> {
    if (!this.audio.device) {
      return {
        ok: false,
        output: 'Pick an audio interface first: the test needs a real device.',
        roundTripMs: null
      };
    }

    await this.host.stop();
    try {
      const host = engine.host();
      const args = [
        ...host.args,
        '--latency-test',
        '--device',
        this.audio.device,
        '--rate',
        String(this.audio.sampleRate),
        '--buffer',
        String(this.audio.bufferFrames),
        '--seconds',
        '3'
      ];
      const output = await new Promise<string>(done =>
        execFile(host.command, args, { timeout: 20_000 }, (error, stdout, stderr) =>
          done(`${stdout}${stderr}${error && !stdout ? error.message : ''}`)
        )
      );
      const match = /measured round trip: \d+ frames = ([\d.]+) ms/.exec(output);
      return { ok: match !== null, output: output.trim(), roundTripMs: match ? Number(match[1]) : null };
    } finally {
      this.host.start();
      this.host.healthy();
    }
  }

  /**
   * Measures an IR with the engine stopped (the device cannot be shared): a sweep out of outputs 1-2, recorded on
   * input 1, then restarts the engine.
   */
  irCapture(options: IrCaptureOptions): Promise<IrCaptureResult> {
    return this.exclusive(() => this.runIrCapture(options));
  }

  private async runIrCapture(options: IrCaptureOptions): Promise<IrCaptureResult> {
    if (!this.audio.device) {
      return { ok: false, output: 'Pick an audio interface first: capturing needs a real device.', wav: null };
    }

    const file = join(tmpdir(), `cathode-ir-${process.pid}-${Date.now()}.wav`);
    await this.host.stop();
    try {
      const host = engine.host();
      const args = [
        ...host.args,
        '--ir-capture',
        '--device',
        this.audio.device,
        '--rate',
        String(this.audio.sampleRate),
        '--buffer',
        String(this.audio.bufferFrames),
        '--seconds',
        String(options.seconds),
        '--level',
        String(options.levelDb),
        '--length',
        String(options.lengthMs),
        '--out',
        file
      ];
      const { output, ok } = await new Promise<{ output: string; ok: boolean }>(done =>
        execFile(host.command, args, { timeout: (options.seconds + 20) * 1_000 }, (error, stdout, stderr) =>
          done({ output: `${stdout}${stderr}${error && !stdout ? error.message : ''}`.trim(), ok: !error })
        )
      );
      const wav = ok ? new Uint8Array(await readFile(file)) : null;
      return { ok: ok && wav !== null, output, wav };
    } finally {
      await rm(file, { force: true });
      this.host.start();
      this.host.healthy();
    }
  }

  private hostCommand(): Command & { env: NodeJS.ProcessEnv } {
    const host = engine.host();
    const audio = this.audio.device ? ['--device', this.audio.device] : ['--null-audio'];
    const loop = [
      ...(this.audio.loopIn.length ? ['--loop-in', this.audio.loopIn.join(',')] : []),
      ...(this.audio.loopOut.length ? ['--loop-out', this.audio.loopOut.join(',')] : []),
      ...(this.audio.outputs?.length ? ['--outputs', this.audio.outputs.join(',')] : []),
      ...(this.audio.diOut?.length ? ['--di-out', this.audio.diOut.join(',')] : []),
      ...inputMap(this.audio.inputs)
    ];
    return {
      command: host.command,
      args: [
        ...host.args,
        '--serve',
        '--socket',
        this.socket,
        '--watch-stdin',
        ...audio,
        ...(this.audio.device ? loop : []),
        '--rate',
        String(this.audio.sampleRate),
        '--buffer',
        String(this.audio.bufferFrames)
      ],
      env: { DOTNET_GCgen0size: Gen0Budget }
    };
  }

  /**
   * The token goes in the environment, never on the command line: other accounts on the machine can read every
   * process's arguments (`ps`, Task Manager), but not its environment.
   */
  private serverCommand(): Command & { env: NodeJS.ProcessEnv } {
    const server = engine.server();
    return {
      command: server.command,
      args: [
        ...server.args,
        `--urls=http://127.0.0.1:${this.port}${this.onNetwork() ? `;http://0.0.0.0:${this.network.port}` : ''}`,
        `--Cathode:Engine:SocketPath=${this.socket}`,
        '--Cathode:WatchStdin=true',
        ...(this.onNetwork() ? [`--Cathode:EditorDirectory=${this.editorDirectory}`] : []),
        ...(this.dataDirectory ? [`--Cathode:DataDirectory=${this.dataDirectory}`] : [])
      ],
      env: { Cathode__Token: this.token() }
    };
  }
}

/** Lists audio interfaces through the host (`--devices --json`). */
export function listDevices(): Promise<{ backend: string; devices: unknown[] }> {
  const host = engine.host();
  return new Promise((done, fail) =>
    execFile(host.command, [...host.args, '--devices', '--json'], { timeout: 15_000 }, (error, stdout) => {
      if (error) {
        // No native backend on this platform yet: the null backend is the only choice.
        done({ backend: 'none', devices: [] });
        return;
      }

      try {
        done(JSON.parse(stdout));
      } catch (parse) {
        fail(parse);
      }
    })
  );
}

function freePort(): Promise<number> {
  return new Promise((done, fail) => {
    const probe = createServer();
    probe.once('error', fail);
    probe.listen(0, '127.0.0.1', () => {
      const address = probe.address();
      probe.close(() => (typeof address === 'object' && address ? done(address.port) : fail(new Error('No port'))));
    });
  });
}

/**
 * Waits for a Server to answer, for `timeoutMs`. A Server that refuses the token (401) fails at once: waiting will not
 * change that. `unanswered` is the error when the time runs out.
 */
export async function waitForServer(
  connection: Connection | { baseUrl: string; token: string | null },
  timeoutMs = ReadyTimeoutMs,
  unanswered = `The Cathode server did not start within ${timeoutMs / 1000} s.`
): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    let response: Response | null = null;
    try {
      response = await fetch(`${connection.baseUrl}/api/v1/system`, {
        headers: connection.token ? { Authorization: `Bearer ${connection.token}` } : {},
        signal: AbortSignal.timeout(2_000)
      });
    } catch {
      // not up yet
    }

    if (response?.ok) {
      return;
    }

    if (response?.status === 401) {
      throw new Error(`The Cathode server at ${connection.baseUrl} refused the access token.`);
    }

    await new Promise(resolve => setTimeout(resolve, 250));
  }

  throw new Error(unanswered);
}

/** `--input-map 1;3,4;;5,6` for cathode-host, or nothing when only Input 1 is on channel 1 (the host's default). */
export function inputMap(inputs: number[][] | undefined): string[] {
  const sources = inputs ?? [];
  const isDefault =
    sources.slice(1).every(s => s.length === 0) && (sources.length === 0 || sources[0].join(',') === '1');
  return isDefault ? [] : ['--input-map', sources.map(s => s.join(',')).join(';')];
}
