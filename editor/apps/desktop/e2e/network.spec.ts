import { chromium } from '@playwright/test';
import { createServer } from 'node:net';
import { expect, test } from './fixtures';

// Control from other devices: the desktop app's Server also on the network, serving the editor, with a token.

const Token = 'e2e-network-token-0123456789';

test('it is off by default and turns on from Audio setup', async ({ window }) => {
  const port = await freePort();
  await window.keyboard.press('ControlOrMeta+Comma');
  await window.getByRole('tab', { name: 'Audio setup' }).click();
  const allow = window.getByRole('switch', { name: 'Allow control from other devices on this network' });
  await expect(allow).not.toBeChecked();
  await expect(fetch(`http://127.0.0.1:${port}/`)).rejects.toThrow();

  await window.getByRole('spinbutton', { name: 'Network port' }).fill(String(port));
  // The window reloads on the restarted Server; the setting then shows where to go.
  await Promise.all([window.waitForEvent('load', { timeout: 60_000 }), allow.click()]);
  await expect(window.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible({ timeout: 60_000 });
  await window.keyboard.press('ControlOrMeta+Comma');
  await window.getByRole('tab', { name: 'Audio setup' }).click();
  await expect(window.getByRole('switch', { name: /Allow control/ })).toBeChecked();
  await expect(window.getByTestId('network-url').first()).toHaveText(new RegExp(`^http://.+:${port}$`));
  await expect(window.getByRole('img', { name: /^QR code for http:.*\?token=/ })).toBeVisible();
  const token = (await window.getByTestId('network-token').textContent())?.trim() ?? '';
  expect(token.length).toBeGreaterThanOrEqual(32);
  await window.getByRole('button', { name: 'Copy' }).click();
  await expect(window.getByText('Copied the access token.')).toBeVisible();

  // The editor is there for anyone; the API only with the token.
  const page = await fetch(`http://127.0.0.1:${port}/`);
  expect(await page.text()).toContain('<meta name="cathode-server" content="same-origin">');
  expect((await fetch(`http://127.0.0.1:${port}/api/v1/system`)).status).toBe(401);
  const system = await fetch(`http://127.0.0.1:${port}/api/v1/system`, {
    headers: { Authorization: `Bearer ${token}` }
  });
  expect(system.status).toBe(200);

  // And off again: nothing listens there.
  await Promise.all([
    window.waitForEvent('load', { timeout: 60_000 }),
    window.getByRole('switch', { name: /Allow control/ }).click()
  ]);
  await expect(window.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible({ timeout: 60_000 });
  await expect(fetch(`http://127.0.0.1:${port}/`)).rejects.toThrow();
});

test.describe('when on', () => {
  let port = 0;
  test.beforeAll(async () => {
    port = await freePort();
  });
  test.use({
    // eslint-disable-next-line no-empty-pattern -- Playwright reads a fixture's dependencies from its first parameter's destructuring; this one has none
    desktopSettings: async ({}, use) => use({ network: { enabled: true, port, token: Token } })
  });

  test('a browser on the network edits the same live rig, and both stay in sync', async ({ editor, window }) => {
    const browser = await chromium.launch();
    try {
      const tablet = await browser.newPage({ viewport: { width: 1180, height: 820 }, hasTouch: true });
      const frames = new Promise<number>(done =>
        tablet.on('websocket', socket => {
          let count = 0;
          socket.on('framereceived', () => ++count === 5 && done(count));
        })
      );
      // As the QR code opens it; the token then leaves the address bar.
      await tablet.goto(`http://127.0.0.1:${port}/?token=${Token}`);
      await expect(tablet.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible({ timeout: 30_000 });
      expect(new URL(tablet.url()).searchParams.has('token')).toBe(false);

      // The desktop adds a block: the tablet sees it.
      await editor.addBlock(2, 2, 'Delay');
      await expect(tablet.getByRole('gridcell', { name: /^Delay delay1/ })).toBeVisible();

      // The tablet bypasses it: the desktop sees that.
      await tablet.getByRole('gridcell', { name: /^Delay delay1/ }).click();
      await tablet.getByRole('grid', { name: 'Signal grid' }).press('Space');
      await expect(window.getByRole('gridcell', { name: /^Delay delay1/ })).toHaveAccessibleName(/bypassed/);

      // Both are on the live stream: connected, and the tablet's WebSocket carries the engine's telemetry.
      for (const page of [window, tablet]) {
        await expect(page.getByText('Connected to the engine')).toBeAttached();
      }

      expect(await frames).toBeGreaterThan(0);
    } finally {
      await browser.close();
    }
  });

  test('a browser without the token is asked for it', async ({ window }) => {
    expect(window).toBeTruthy(); // the app is up
    const browser = await chromium.launch();
    try {
      const page = await browser.newPage();
      await page.goto(`http://127.0.0.1:${port}/`);
      await expect(page.getByText(/access token/i).first()).toBeVisible({ timeout: 30_000 });
      await expect(page.getByRole('gridcell', { name: /^Input in1/ })).toHaveCount(0);
    } finally {
      await browser.close();
    }
  });
});

function freePort(): Promise<number> {
  return new Promise((done, fail) => {
    const probe = createServer();
    probe.once('error', fail);
    probe.listen(0, '0.0.0.0', () => {
      const address = probe.address();
      probe.close(() => (typeof address === 'object' && address ? done(address.port) : fail(new Error('No port'))));
    });
  });
}
