import { type ChildProcess, spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import type { Command } from './engine';

export interface SidecarStatus {
  name: 'host' | 'server';
  /**
   * `running` once the process has said it serves. `restarting` after it exited on its own; `failed` once it has done
   * so {@link SidecarTiming.failedAfter} times in a row without serving in between (it is still retried, so an
   * interface that comes back is picked up), or when it cannot be started at all.
   */
  state: 'starting' | 'running' | 'restarting' | 'stopped' | 'failed';
  detail: string | null;
  /** The running process's id, or null. */
  pid: number | null;
}

export interface SidecarTiming {
  /** The wait before the first restart; it doubles with each exit in a row. */
  backoffMs: number;
  maxBackoffMs: number;
  /** This many exits in a row without the process serving in between, and it is reported as failed. */
  failedAfter: number;
  /** How long a process asked to stop may take before it is killed. */
  killAfterMs: number;
}

const DefaultTiming: SidecarTiming = { backoffMs: 500, maxBackoffMs: 10_000, failedAfter: 5, killAfterMs: 3_000 };
/** How many of its last output lines are kept for the editor to show. */
const LogLines = 200;

/**
 * Asks a child that watches its standard input (`--watch-stdin`, `Cathode:WatchStdin`) to stop, and resolves once it
 * has exited; it is killed if it is still running `killAfterMs` later.
 */
export async function endPolitely(child: ChildProcess, killAfterMs = DefaultTiming.killAfterMs): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null || child.pid === undefined) {
    return;
  }

  const exited = new Promise<void>(done => child.once('exit', () => done()));
  // Closing its lifeline asks it to stop, and it then stops cleanly: the host releases the device and removes its
  // socket. On Windows that is the only polite way: Node has no signals there, and kill() terminates outright.
  child.stdin?.end();
  if (process.platform !== 'win32') {
    child.kill('SIGTERM');
  }

  const killer = setTimeout(() => child.kill('SIGKILL'), killAfterMs);
  await exited;
  clearTimeout(killer);
}

/**
 * One supervised child process: started with a command line computed at each (re)start, restarted with backoff
 * when it exits on its own, stopped politely on request (its standard input closed, and SIGTERM where there are
 * signals), and killed if it is still running 3 s later.
 *
 * Its own ready line is what says it runs: being spawned proves nothing (a host that cannot open the audio interface
 * exits a moment later). The ready line also starts the backoff over, so an occasional crash in a long session is
 * always restarted quickly, and only exits in a row wait longer.
 */
export class Sidecar {
  private child: ChildProcess | null = null;
  private wanted = false;
  /** Exits in a row since the process last served (or was last stopped on request). */
  private failures = 0;
  private restartTimer: NodeJS.Timeout | null = null;
  private readonly timing: SidecarTiming;
  readonly log: string[] = [];
  status: SidecarStatus;

  constructor(
    readonly name: 'host' | 'server',
    private readonly commandLine: () => Command & { env?: NodeJS.ProcessEnv },
    /** What the process prints once it serves: running (again, after a restart). */
    private readonly ready: RegExp,
    private readonly onStatus: () => void,
    timing: Partial<SidecarTiming> = {},
    /** Gets every line the process prints, to keep beyond the last few: the log file. */
    private readonly onLine: (line: string) => void = () => undefined
  ) {
    this.timing = { ...DefaultTiming, ...timing };
    this.status = { name, state: 'stopped', detail: null, pid: null };
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
    // What is started next is a new attempt (new settings, usually): it starts the backoff over.
    this.failures = 0;
    if (this.restartTimer) {
      clearTimeout(this.restartTimer);
      this.restartTimer = null;
    }

    if (this.child) {
      await endPolitely(this.child, this.timing.killAfterMs);
    }

    this.set('stopped', null);
  }

  /**
   * Called when the process has proved healthy by other means than its ready line (the Server answered a request).
   * Nothing is taken on trust: a process that does not run is not marked running.
   */
  healthy(): void {
    if (this.child) {
      this.failures = 0;
      this.set('running', null);
    }
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

    const child = spawn(command.command, command.args, {
      env: { ...process.env, ...command.env },
      // Standard input is the child's lifeline (StdinWatch in the engine): the OS closes it however this app ends, a
      // crash or a force quit included, and the child then stops on its own instead of outliving the app.
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true
    });
    this.child = child;
    if (this.failures >= this.timing.failedAfter) {
      this.set('failed', this.status.detail); // still failed, and why, until this attempt serves
    } else {
      this.set(this.failures === 0 ? 'starting' : 'restarting', null);
    }

    child.stdin.on('error', () => undefined); // a child that already exited: nothing to tell it
    /** The last thing this process said: why it exited, usually. */
    let lastLine: string | null = null;
    const keep = (line: string) => {
      if (line.trim() === '') {
        return;
      }

      lastLine = line;
      this.remember(line);
      if (this.child === child && this.status.state !== 'running' && this.ready.test(line)) {
        this.failures = 0;
        this.set('running', null);
      }
    };
    // Whole lines, however the output is cut into chunks: the ready line must not be missed for arriving in two.
    for (const stream of [child.stdout, child.stderr]) {
      createInterface({ input: stream, crlfDelay: Infinity }).on('line', keep);
    }

    const ended = (code: number | string | null) => {
      if (this.child !== child) {
        return;
      }

      this.child = null;
      if (!this.wanted) {
        return;
      }

      const failures = ++this.failures;
      const delay = Math.min(this.timing.maxBackoffMs, this.timing.backoffMs * 2 ** (failures - 1));
      const timer = setTimeout(() => this.launch(), delay);
      this.restartTimer = timer;
      const seconds = Math.round(delay / 1000);
      const report = () => {
        if (this.restartTimer !== timer) {
          return; // restarted or stopped since
        }

        if (failures >= this.timing.failedAfter) {
          const why = lastLine === null ? '' : `: ${lastLine}`;
          this.set('failed', `exited (${code}) ${failures} times in a row${why}; trying again in ${seconds} s`);
        } else {
          this.set('restarting', `exited (${code}); restarting in ${seconds} s`);
        }
      };
      report();
      // The exit can arrive before the last of the output has been read: say again once it has, with the real last line.
      child.once('close', report);
    };
    child.once('exit', (code, signal) => ended(signal ?? code));
    // A process that never started emits no exit: it ends here, so it is retried like one that crashed.
    child.once('error', error => {
      keep(`spawn failed: ${error.message}`);
      if (child.pid === undefined) {
        ended(error.message);
      }
    });
  }

  private remember(line: string): void {
    this.onLine(line);
    this.log.push(line);
    this.log.splice(0, Math.max(0, this.log.length - LogLines));
  }

  private set(state: SidecarStatus['state'], detail: string | null): void {
    this.status = { name: this.name, state, detail, pid: this.child?.pid ?? null };
    this.onStatus();
  }
}
