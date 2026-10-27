import type { ElectronApplication, Page } from '@playwright/test';
import { execFileSync, spawn } from 'node:child_process';
import { readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { expect, test } from './fixtures';

// The engine sidecars as processes: what other programs on the machine can see of them.

test('the access token is not on any command line', async ({ window }) => {
  const { token } = await connection(window);
  expect(token.length).toBeGreaterThanOrEqual(32);
  const lines = commandLines();
  expect(lines.some(line => line.includes('cathode-server'))).toBe(true);
  expect(lines.filter(line => line.includes(token))).toEqual([]);
});

test('overlapping engine restarts leave one engine running', async ({ app, window }) => {
  const pid = await mainPid(app);
  expect(hosts(pid)).toHaveLength(1);
  // Two Applies at once (a double click): each stops the host and starts it again.
  await window.evaluate(async () => {
    const desktop = (globalThis as unknown as { cathode: { audio: Desktop['audio'] } }).cathode;
    const settings = await desktop.audio.settings();
    await Promise.all([desktop.audio.apply(settings), desktop.audio.apply(settings)]);
  });
  await expect(window.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible();
  // And it stays one: a second host would fight the first for the socket (and the device), one of them restarting.
  await expect.poll(() => hosts(pid).length, { timeout: 10_000 }).toBe(1);
  const counts: number[] = [];
  for (let i = 0; i < 8; i++) {
    await new Promise(resolve => setTimeout(resolve, 500));
    counts.push(hosts(pid).length);
  }
  expect(counts).toEqual([1, 1, 1, 1, 1, 1, 1, 1]);
});

test('the engine stops on its own when the app is killed', async ({ app, window }) => {
  const { baseUrl } = await connection(window);
  const pid = await mainPid(app);
  expect(hosts(pid)).toHaveLength(1);
  expect(servers(pid)).toHaveLength(1);
  expect(socketExists(pid)).toBe(true);

  // A crash or a force quit: no before-quit, nobody stops the sidecars.
  process.kill(pid, 'SIGKILL');
  await expect.poll(() => hosts(pid).length + servers(pid).length, { timeout: 30_000 }).toBe(0);
  await expect(fetch(`${baseUrl}/api/v1/system`)).rejects.toThrow();
  // On macOS and Linux they saw their standard input close and stopped cleanly: the host removed its socket. Windows
  // kills them outright with the app (Node starts children in a job object that ends with it), leaving the socket.
  if (process.platform !== 'win32') {
    expect(socketExists(pid)).toBe(false);
  }
});

test('quitting stops the engine cleanly, on every platform', async ({ app, window }) => {
  await expect(window.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible();
  const pid = await mainPid(app);
  expect(socketExists(pid)).toBe(true);

  await app.close();
  expect(hosts(pid)).toEqual([]);
  expect(servers(pid)).toEqual([]);
  // The host had time to release the device and remove its socket: it was asked to stop, not terminated.
  expect(socketExists(pid)).toBe(false);
});

test('launching it again brings the running app forward instead of starting a second engine', async ({
  app,
  window
}) => {
  await expect(window.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible();
  const userData = await app.evaluate(({ app }) => app.getPath('userData'));
  const noticed = app.evaluate(
    ({ app }) => new Promise<boolean>(done => app.once('second-instance', () => done(true)))
  );

  // Linux CI has no setuid sandbox helper, so Electron needs --no-sandbox there, as Playwright passes to the first app.
  const sandbox = process.platform === 'linux' ? ['--no-sandbox'] : [];
  // In Node, the electron package exports the path of its binary, which its typings (the Electron API) do not describe.
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- require() is how that path is read
  const binary = require('electron') as string;
  const second = spawn(binary, [...sandbox, resolve(__dirname, '../../../dist/apps/desktop/main.js')], {
    env: { ...process.env, CATHODE_USER_DATA: userData },
    stdio: 'ignore'
  });
  const exitCode = await new Promise<number | null>(done => second.once('exit', code => done(code)));

  expect(exitCode).toBe(0);
  expect(await noticed).toBe(true);
  expect(hosts(await mainPid(app))).toHaveLength(1);
});

test('the editor shows when the engine host is down, and its log says why', async ({ window }) => {
  const status = window.getByTestId('engine-status');
  await expect(status).toContainText('Connected to the engine');
  await window.keyboard.press('ControlOrMeta+Comma');
  await window.getByRole('tab', { name: 'Audio setup' }).click();
  const processes = window.getByTestId('engine-processes');
  await expect(processes).toContainText('Engine host: running');
  await expect(processes).toContainText('Server: running');
  await window.getByRole('button', { name: 'Show engine log' }).click();
  await expect(window.getByRole('log', { name: 'Engine log' })).toContainText(/cathode-host .* serving on/);

  // The host crashes: the Server still answers, but the light no longer says all is well, until the host is back.
  await window.evaluate(() => {
    const seen: string[] = [];
    (globalThis as unknown as { seen: string[] }).seen = seen;
    const light = document.querySelector('[data-testid="engine-status"]');
    if (!light) {
      throw new Error('No engine status light');
    }

    new MutationObserver(() => seen.push(light.textContent.trim())).observe(light, {
      subtree: true,
      characterData: true,
      childList: true
    });
  });
  const statuses = (await window.evaluate(() =>
    (globalThis as unknown as { cathode: { sidecars(): Promise<unknown> } }).cathode.sidecars()
  )) as { name: string; pid: number }[];
  const host = statuses.find(s => s.name === 'host');
  if (!host) {
    throw new Error('No engine host process');
  }

  process.kill(host.pid, 'SIGKILL');

  const seen = () => window.evaluate(() => (globalThis as unknown as { seen: string[] }).seen);
  await expect.poll(async () => (await seen()).some(text => text.startsWith('The engine stopped'))).toBe(true);
  await expect(status).toContainText('Connected to the engine', { timeout: 30_000 });
  await expect(processes).toContainText('Engine host: running');
});

interface Desktop {
  audio: { settings(): Promise<unknown>; apply(settings: unknown): Promise<void> };
}

/** The app's main process (which, on Windows, is not the one Playwright started). */
function mainPid(app: ElectronApplication): Promise<number> {
  return app.evaluate(() => process.pid);
}

/** The socket between the host and Server of the app whose main process is `pid`. */
function socket(pid: number): string {
  return join(tmpdir(), `cathode-${pid}.sock`);
}

/** Whether that socket is there: listed, as Windows cannot stat a Unix socket. */
function socketExists(pid: number): boolean {
  return readdirSync(tmpdir()).includes(`cathode-${pid}.sock`);
}

/** The engine hosts that app runs (on its socket), by command line. */
function hosts(pid: number): string[] {
  return commandLines().filter(
    line => line.includes('cathode-host') && line.includes('--serve') && line.includes(socket(pid))
  );
}

/** The Servers that app runs (on its host's socket), by command line. */
function servers(pid: number): string[] {
  return commandLines().filter(line => line.includes('cathode-server') && line.includes(socket(pid)));
}

/** The connection the desktop app hands its editor: the local Server's address and token. */
function connection(window: Page): Promise<{ baseUrl: string; token: string }> {
  return window.evaluate(() =>
    (
      globalThis as unknown as { cathode: { connection(): Promise<{ baseUrl: string; token: string }> } }
    ).cathode.connection()
  );
}

/** Every process's command line, as any account on the machine can list them. */
function commandLines(): string[] {
  const output =
    process.platform === 'win32'
      ? execFileSync(
          'powershell.exe',
          ['-NoProfile', '-Command', 'Get-CimInstance Win32_Process | ForEach-Object { $_.CommandLine }'],
          { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }
        )
      : execFileSync('ps', ['-ewwo', 'args'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return output.split(/\r?\n/).filter(Boolean);
}
