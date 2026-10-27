import { execFileSync } from 'node:child_process';
import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';

// The engine sidecars as processes: what other programs on the machine can see of them.

test('the access token is not on any command line', async ({ window }) => {
  const { token } = await connection(window);
  expect(token.length).toBeGreaterThanOrEqual(32);
  const lines = commandLines();
  expect(lines.some(line => line.includes('cathode-server'))).toBe(true);
  expect(lines.filter(line => line.includes(token))).toEqual([]);
});

/** The connection the desktop app hands its editor: the local Server's address and token. */
function connection(window: Page): Promise<{ baseUrl: string; token: string }> {
  return window.evaluate(() =>
    (globalThis as unknown as { cathode: { connection(): Promise<{ baseUrl: string; token: string }> } }).cathode.connection()
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
