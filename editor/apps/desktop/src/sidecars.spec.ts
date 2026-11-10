import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defaultSettings } from './settings-store';
import { type SidecarStatus, Sidecars } from './sidecars';

const fake = vi.hoisted(() => ({ script: '' }));

// The engine is a Node script here (engine.ts needs Electron to find the real one).
vi.mock('./engine', () => ({
  engine: {
    host: () => ({ command: process.execPath, args: [fake.script, 'host'] }),
    server: () => ({ command: process.execPath, args: [fake.script, 'server'] })
  }
}));

/**
 * Stands in for cathode-host and cathode-server. The host serves until its standard input closes; the Server answers
 * on its loopback port; a measurement writes its arguments to a file, then runs for FAKE_MEASURE_MS unless its
 * standard input closes first (with --watch-stdin), and writes how it ended.
 */
const FakeEngine = `
const { writeFileSync } = require('node:fs');
const { createServer } = require('node:http');
const [role, ...args] = process.argv.slice(2);
const notes = process.env.FAKE_ENGINE_NOTES;
process.on('SIGTERM', () => undefined); // only the lifeline counts here
if (role === 'server') {
  const port = Number(/127\\.0\\.0\\.1:(\\d+)/.exec(args.find(a => a.startsWith('--urls=')))[1]);
  createServer((_request, response) => response.end('{}')).listen(port, '127.0.0.1', () =>
    console.log('Now listening on: http://127.0.0.1:' + port));
  process.stdin.on('end', () => process.exit(0)).resume();
} else if (args.includes('--serve')) {
  console.log('cathode-host fake serving on a socket');
  process.stdin.on('end', () => process.exit(0)).resume();
} else {
  writeFileSync(notes + '.started', JSON.stringify(args));
  const whole = setTimeout(() => {
    writeFileSync(notes + '.ended', 'ran to the end');
    console.log('measured round trip: 96 frames = 2.00 ms');
    process.exit(0);
  }, Number(process.env.FAKE_MEASURE_MS));
  if (args.includes('--watch-stdin')) {
    process.stdin.on('end', () => {
      clearTimeout(whole);
      writeFileSync(notes + '.ended', 'standard input closed');
      console.log('Stopped: standard input closed (--watch-stdin).');
      process.exit(1);
    }).resume();
  }
}
`;

const StoppedMessage = 'The local engine was stopped.';

describe('Sidecars', () => {
  let folder: string;
  let notes: string;
  let sidecars: Sidecars;
  let seen: SidecarStatus[][];

  const read = (what: 'started' | 'ended') => {
    try {
      return readFileSync(`${notes}.${what}`, 'utf8');
    } catch {
      return null;
    }
  };
  const states = () => sidecars.statuses().map(status => status.state);
  const until = (met: () => boolean) => expect.poll(met, { timeout: 15_000, interval: 10 }).toBe(true);
  const pause = (ms: number) => new Promise(done => setTimeout(done, ms));

  beforeEach(() => {
    folder = mkdtempSync(join(tmpdir(), 'cathode-sidecars-'));
    notes = join(folder, 'measurement');
    fake.script = join(folder, 'fake-engine.cjs');
    writeFileSync(fake.script, FakeEngine);
    vi.stubEnv('FAKE_ENGINE_NOTES', notes);
    vi.stubEnv('FAKE_MEASURE_MS', '10000');
    seen = [];
    sidecars = new Sidecars(
      { ...defaultSettings().audio, device: 'interface-1' },
      defaultSettings().network,
      folder,
      null,
      statuses => seen.push(statuses)
    );
  });

  afterEach(async () => {
    await sidecars.stop();
    vi.unstubAllEnvs();
    rmSync(folder, { recursive: true, force: true });
  });

  it('runs the host and the Server, each running once it says so', async () => {
    const connection = await sidecars.start();

    expect(connection.baseUrl).toMatch(/^http:\/\/127\.0\.0\.1:\d+$/);
    await until(() => states().join() === 'running,running');
  });

  it('stops the host for a latency test and starts it again afterwards', async () => {
    vi.stubEnv('FAKE_MEASURE_MS', '200');
    await sidecars.start();
    await until(() => states().join() === 'running,running');

    const test = sidecars.latencyTest();
    await until(() => read('started') !== null);
    expect(states()[0]).toBe('stopped'); // the device cannot be shared

    expect(await test).toEqual({ ok: true, output: 'measured round trip: 96 frames = 2.00 ms', roundTripMs: 2 });
    expect(read('ended')).toBe('ran to the end');
    await until(() => states()[0] === 'running');
  });

  it('ends a latency test under way when it is stopped, and does not start the host again', async () => {
    await sidecars.start();
    const test = sidecars.latencyTest();
    await until(() => read('started') !== null);
    expect(JSON.parse(read('started') ?? '[]')).toEqual(expect.arrayContaining(['--latency-test', '--watch-stdin']));

    await sidecars.stop();

    // The measurement was asked to stop and did, long before its ten seconds, and stop() waited for it.
    expect(read('ended')).toBe('standard input closed');
    expect(await test).toMatchObject({ ok: false, roundTripMs: null });
    expect(states()).toEqual(['stopped', 'stopped']);
    seen = [];
    await pause(300);
    expect(seen).toEqual([]);
  });

  it('runs no job that was still queued when it was stopped', async () => {
    await sidecars.start();
    const test = sidecars.latencyTest();
    const capture = sidecars.irCapture({ seconds: 1, levelDb: -18, lengthMs: 500 });
    const apply = sidecars.applyAudio({ ...defaultSettings().audio, device: 'interface-2' });
    await until(() => read('started') !== null);
    rmSync(`${notes}.started`);

    await sidecars.stop();

    expect(await test).toMatchObject({ ok: false });
    expect(await capture).toEqual({ ok: false, output: StoppedMessage, wav: null });
    await apply;
    expect(read('started')).toBeNull(); // the capture never ran
    expect(states()).toEqual(['stopped', 'stopped']);
  });

  it('stays stopped: nothing starts a process afterwards', async () => {
    await sidecars.start();
    await sidecars.stop();

    await expect(sidecars.start()).rejects.toThrow(StoppedMessage);
    await sidecars.applyAudio(defaultSettings().audio);
    expect(await sidecars.latencyTest()).toEqual({ ok: false, output: StoppedMessage, roundTripMs: null });
    await pause(100);
    expect(states()).toEqual(['stopped', 'stopped']);
  });

  it('starts nothing when it is stopped while still starting', async () => {
    const starting = sidecars.start();
    const stopping = sidecars.stop();

    await expect(starting).rejects.toThrow(StoppedMessage);
    await stopping;
    await pause(100);
    expect(states()).toEqual(['stopped', 'stopped']);
  });
});
