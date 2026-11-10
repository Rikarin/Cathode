import { afterEach, describe, expect, it } from 'vitest';
import { Sidecar, type SidecarStatus } from './sidecar';

/** A stand-in for cathode-host: a Node script, ended by closing its standard input as the real one is. */
const node = (script: string) => () => ({
  command: process.execPath,
  args: ['-e', `process.stdin.on('end', () => process.exit(0)).resume(); ${script}`]
});

/** Serves at once and stays. */
const Serves = `console.log('cathode-host 1.0 serving on /tmp/x.sock');`;
/** Cannot open its device: says why and exits. */
const Exits = `console.error('error: no such device'); process.exit(1);`;
/** Serves, then crashes a moment later, every time. */
const ServesThenCrashes = `${Serves} setTimeout(() => process.exit(3), 30);`;
/** The ready line in two writes, as a pipe may deliver it. */
const ServesInTwoChunks = `process.stdout.write('cathode-host 1.0 serv'); setTimeout(() => console.log('ing on /tmp/x.sock'), 50);`;

const Fast = { backoffMs: 10, maxBackoffMs: 40, failedAfter: 3, killAfterMs: 2_000 };

describe('Sidecar', () => {
  let sidecar: Sidecar | null = null;
  let seen: SidecarStatus[] = [];

  const supervise = (commandLine: ReturnType<typeof node>) => {
    seen = [];
    const made: Sidecar = new Sidecar('host', commandLine, / serving on /, () => seen.push(made.status), Fast);
    sidecar = made;
    return made;
  };
  const states = () => seen.map(status => status.state);
  const until = (met: () => boolean) => expect.poll(met, { timeout: 10_000, interval: 5 }).toBe(true);

  afterEach(async () => {
    await sidecar?.stop();
    sidecar = null;
  });

  it('is starting until the process says it serves, and running from then on', async () => {
    const host = supervise(node(`setTimeout(() => { ${Serves} }, 100);`));
    host.start();

    expect(host.status).toMatchObject({ state: 'starting', pid: expect.any(Number) as unknown });
    await until(() => host.status.state === 'running');
    expect(states()).toEqual(['starting', 'running']);
  });

  it('sees a ready line that arrives in two chunks', async () => {
    const host = supervise(node(ServesInTwoChunks));
    host.start();

    await until(() => host.status.state === 'running');
    expect(host.log).toEqual(['cathode-host 1.0 serving on /tmp/x.sock']);
  });

  it('never calls a process that keeps exiting running, and reports it failed with its last line', async () => {
    const host = supervise(node(Exits));
    host.start();

    await until(() => host.status.state === 'failed' && (host.status.detail ?? '').includes('no such device'));
    expect(states()).not.toContain('running');
    expect(host.status.detail).toMatch(/^exited \(1\) \d+ times in a row: error: no such device; trying again in/);

    // Still retried, so an interface that comes back is picked up; and still failed while it is.
    const attempts = seen.filter(status => status.pid !== null).length;
    await until(() => seen.filter(status => status.pid !== null).length > attempts + 1);
    expect(states().slice(states().indexOf('failed'))).toEqual(expect.not.arrayContaining(['starting', 'restarting']));
  });

  it('does not take a process that exited for a healthy one', async () => {
    const host = supervise(node(Exits));
    host.start();
    await until(() => host.status.state === 'restarting' && !host.running);

    host.healthy();
    expect(host.status.state).toBe('restarting');
  });

  it('starts the backoff over each time the process serves again', async () => {
    const host = supervise(node(ServesThenCrashes));
    host.start();

    // More crashes than failedAfter, each after serving: every one is the first in a row.
    await until(() => states().filter(state => state === 'running').length > Fast.failedAfter + 2);
    expect(states()).not.toContain('failed');
    const waits = new Set(seen.filter(status => status.state === 'restarting' && status.detail).map(s => s.detail));
    expect([...waits]).toEqual(['exited (3); restarting in 0 s']);
  });

  it('recovers from failed once the process serves', async () => {
    let command = node(Exits);
    const host = supervise(() => command());
    host.start();
    await until(() => host.status.state === 'failed');

    command = node(Serves);
    await until(() => host.status.state === 'running');
    expect(host.status.detail).toBeNull();
  });

  it('starts over after it was stopped on request', async () => {
    const host = supervise(node(Exits));
    host.start();
    await until(() => host.status.state === 'failed');

    await host.stop();
    expect(host.status).toEqual({ name: 'host', state: 'stopped', detail: null, pid: null });
    seen = [];
    host.start();
    expect(states()).toEqual(['starting']);
  });

  it('reports a command that cannot be found as failed', () => {
    const host = supervise(() => {
      throw new Error('cathode-host not found.');
    });
    host.start();

    expect(host.status).toEqual({ name: 'host', state: 'failed', detail: 'cathode-host not found.', pid: null });
  });
});
