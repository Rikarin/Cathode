import type { ElectronApplication, Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
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

/** The engine hosts that app runs (on its socket), by command line. */
function hosts(pid: number): string[] {
  return commandLines().filter(
    line => line.includes('cathode-host') && line.includes('--serve') && line.includes(socket(pid))
  );
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
