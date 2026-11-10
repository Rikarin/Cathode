import type { Locator, Page } from '@playwright/test';
import { mkdirSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { type Editor, expect, test } from './fixtures';

// The manual's screenshots (website/public/screenshots), taken from the real app: each test stages what its chapter
// describes and saves a PNG to dist/manual-screenshots. They are not part of the test run; take them with
//
//   cd editor
//   pnpm exec nx build desktop && (cd ../engine && ./native/build.sh && dotnet build -c Release)
//   CATHODE_SCREENSHOTS=1 pnpm exec playwright test --config apps/desktop/e2e/playwright.config.ts manual-screenshots
//   for f in dist/manual-screenshots/*.png; do
//     cwebp -quiet -q 82 -resize 1856 0 "$f" -o "../website/public/screenshots/$(basename "$f" .png).webp"
//   done
//
// (a picture narrower than 1856 pixels is not enlarged: leave -resize out for it). The app runs on the null audio
// backend here, so nothing that needs sound through an interface can be staged: a Preset Leveling measurement, an IR
// capture's report and MIDI Monitor traffic are still to be taken by hand on a rig. So is the editor in a phone's
// browser: at a phone's width the preset bar's controls overlap, which is not a picture for the manual.
test.skip(!process.env['CATHODE_SCREENSHOTS'], 'takes the manual screenshots: set CATHODE_SCREENSHOTS=1');

const out = resolve(__dirname, '../../../dist/manual-screenshots');
mkdirSync(out, { recursive: true });

interface Answer {
  status: number;
  body: unknown;
}

/** Calls the app's own Server from its window, with the connection (and token) the editor uses. */
async function api(window: Page, method: string, path: string, body?: unknown): Promise<Answer> {
  const answer = await window.evaluate(
    async ([m, p, b]) => {
      const desktop = globalThis as unknown as {
        cathode: { connection(): Promise<{ baseUrl: string; token: string | null }> };
      };
      const connection = await desktop.cathode.connection();
      const response = await fetch(`${connection.baseUrl}/api/v1${p}`, {
        method: m,
        headers: {
          ...(connection.token ? { Authorization: `Bearer ${connection.token}` } : {}),
          ...(b === null ? {} : { 'Content-Type': 'application/json' })
        },
        body: b === null ? null : JSON.stringify(b)
      });
      const text = await response.text();
      return { status: response.status, text };
    },
    [method, path, body ?? null] as const
  );
  expect(answer.status, `${method} ${path}: ${answer.text}`).toBeLessThan(300);
  return { status: answer.status, body: answer.text ? (JSON.parse(answer.text) as unknown) : null };
}

/** Saves a picture of `target` once the page has settled (meters and transitions included). */
async function shoot(window: Page, target: Locator | Page, name: string): Promise<void> {
  await window.waitForTimeout(700);
  await target.screenshot({ path: join(out, `${name}.png`), animations: 'disabled' });
}

async function go(window: Page, page: string): Promise<void> {
  await window.evaluate(hash => {
    location.hash = hash;
  }, `#/${page}`);
  await window.waitForTimeout(800);
}

/** Loads the factory preset in a slot of the factory bank. */
async function loadFactory(window: Page, name: string): Promise<void> {
  const banks = (await api(window, 'GET', '/banks')).body as { id: string; slots: (string | null)[] }[];
  const presets = (await api(window, 'GET', '/presets')).body as { id: string; name: string }[];
  const id = presets.find(p => p.name === name)?.id;
  const slot = banks.find(b => b.id === 'factory')?.slots.indexOf(id ?? '') ?? -1;
  expect(slot, `the factory preset “${name}”`).toBeGreaterThanOrEqual(0);
  await api(window, 'PUT', `/live/banks/factory/slots/${slot}`, {});
  await window.waitForTimeout(800);
}

async function openBlock(editor: Editor, name: string): Promise<Locator> {
  await editor.window
    .getByRole('gridcell', { name: new RegExp(`^${name} `) })
    .first()
    .click();
  const page = editor.window.getByRole('region', { name: `${name} settings` });
  await expect(page).toBeVisible();
  return page;
}

test('the Amp block on its Power Amp page', async ({ window, editor }) => {
  await loadFactory(window, 'Brown Sound');
  const page = await openBlock(editor, 'Amp');
  await page.getByRole('tab', { name: 'Power Amp' }).click();
  await shoot(window, page, 'power-amp');
});

test('the Cab block with two mics, on its Align view', async ({ window, editor }) => {
  await loadFactory(window, 'Brown Sound');
  const page = await openBlock(editor, 'Cab');
  await page.getByRole('radio', { name: /^Mic 2/ }).click();
  const mute = page.getByRole('switch', { name: 'Mic 2 Mute' });
  if ((await mute.getAttribute('aria-checked')) === 'true') {
    await mute.click();
  }

  await editor.choose('Mic 2 Mic', 'Ribbon 121 (R-121)');
  await editor.setKnob('Mic 2 Distance', '4');
  await page.getByRole('radio', { name: 'Align' }).click();
  // The drawer taller, so the Align view's traces show whole.
  const handle = await window.getByRole('separator', { name: 'Resize panel 1' }).boundingBox();
  if (handle) {
    await window.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
    await window.mouse.down();
    await window.mouse.move(handle.x + handle.width / 2, 190, { steps: 8 });
    await window.mouse.up();
  }

  await shoot(window, page, 'cab-align');
});

test('the Amp NAM block with a capture loaded', async ({ window, editor }) => {
  // One of NeuralAmpModelerCore's example models (MIT), which the native build copies beside the shims.
  const models = resolve(__dirname, '../../../../engine/artifacts/native/nam-models');
  const model = readdirSync(models).find(file => file.endsWith('.nam'));
  expect(model, `a .nam file in ${models} (run engine/native/build.sh)`).toBeTruthy();
  await editor.addBlock(2, 3, 'Amp NAM');
  await editor.addBlock(2, 4, 'Cab');
  const page = await openBlock(editor, 'Amp NAM');
  await page
    .locator('input[type=file]')
    .first()
    .setInputFiles(join(models, model ?? ''));
  await expect(page.getByRole('combobox').filter({ hasText: /\.nam/ }).first()).toBeVisible();
  await shoot(window, page, 'amp-nam');
});

test('a four-cable-method preset on the grid', async ({ window, editor }) => {
  const chain = ['Wah', 'Drive', 'FX Loop', 'Chorus', 'Delay', 'Reverb'];
  for (const [i, block] of chain.entries()) {
    await editor.addBlock(2, 2 + i * 2, block);
  }

  await shoot(window, editor.grid, 'four-cable');
});

test('the Scenes page', async ({ window }) => {
  await loadFactory(window, 'Comfortably Numb Solo');
  await go(window, 'scenes');
  // The page down to the note under the table, not the empty rest of the window.
  const main = await window.getByRole('main').boundingBox();
  const note = await window.getByText(/Scene Ignore: while a block/).boundingBox();
  expect(main && note).toBeTruthy();
  await window.waitForTimeout(700);
  await window.screenshot({
    path: join(out, 'scenes.png'),
    clip: {
      x: 0,
      y: main?.y ?? 0,
      width: main?.width ?? 0,
      height: (note?.y ?? 0) + (note?.height ?? 0) + 24 - (main?.y ?? 0)
    }
  });
});

test('the Controllers page', async ({ window }) => {
  await loadFactory(window, 'Brown Sound');
  await go(window, 'controllers');
  await shoot(window, window.getByRole('main'), 'controllers');
  // The page is longer than the window: its second half, from the LFOs down.
  await window.getByText('LFO 1', { exact: true }).first().scrollIntoViewIfNeeded();
  await window.mouse.wheel(0, 500);
  await shoot(window, window.getByRole('main'), 'controllers-sources');
});

test('the Global page: MIDI mappings and a footswitch layout', async ({ window }) => {
  const global = (await api(window, 'GET', '/global')).body as Record<string, unknown>;
  const tap = (action: string, value?: number) => ({ action, ...(value === undefined ? {} : { value }) });
  await api(window, 'PUT', '/global', {
    ...global,
    midiMappings: [
      { id: 'm1', message: 'pc', channel: 0, number: 0, action: 'presetSlot', target: 'factory' },
      { id: 'm2', message: 'cc', channel: 1, number: 34, action: 'scene' },
      { id: 'm3', message: 'cc', channel: 1, number: 64, action: 'tap' },
      { id: 'm4', message: 'cc', channel: 1, number: 11, action: 'external', value: 1 },
      { id: 'm5', message: 'cc', channel: 1, number: 68, action: 'tuner' }
    ],
    footswitches: [
      {
        id: 'board',
        name: 'Pedalboard',
        views: ['Scenes', 'Looper'],
        switches: [
          { index: 0, label: 'Clean', message: 'cc', channel: 1, number: 20, tap: tap('scene', 0), view: 0 },
          { index: 1, label: 'Crunch', message: 'cc', channel: 1, number: 21, tap: tap('scene', 1), view: 0 },
          {
            index: 2,
            label: 'Lead',
            message: 'cc',
            channel: 1,
            number: 22,
            tap: tap('scene', 2),
            hold: tap('tuner'),
            view: 0
          },
          {
            index: 3,
            label: 'Tap',
            message: 'cc',
            channel: 1,
            number: 23,
            tap: tap('tap'),
            hold: tap('viewNext'),
            view: 0
          }
        ]
      }
    ],
    activeLayout: 'board'
  });
  await go(window, 'global');
  const midi = window.getByRole('region', { name: 'MIDI' });
  await midi.scrollIntoViewIfNeeded();
  await shoot(window, midi, 'midi');
  const switches = window.getByRole('region', { name: 'Footswitches' });
  await switches.scrollIntoViewIfNeeded();
  await shoot(window, switches, 'footswitches');
});

test('the Manage presets page', async ({ window }) => {
  const presets = (await api(window, 'GET', '/presets')).body as { id: string; name: string }[];
  const names = ['Brown Sound', 'Back in Black', 'Comfortably Numb Solo', 'Everlong', 'Creep', 'Cliffs of Dover'];
  const ids = names.map(name => presets.find(p => p.name === name)?.id ?? null);
  await api(window, 'PUT', '/banks/gig', { id: 'gig', name: 'Friday gig', slots: [...ids, null, ids[0]] });
  await api(window, 'PUT', '/banks/practice', { id: 'practice', name: 'Practice', slots: [ids[3], ids[4]] });
  await api(window, 'PATCH', `/presets/${ids[0]}/metadata`, { name: null, color: 'red', favorite: true });
  await api(window, 'PATCH', `/presets/${ids[2]}/metadata`, { name: null, color: 'deepskyblue', favorite: null });
  await api(window, 'PATCH', `/presets/${ids[3]}/metadata`, { name: null, color: null, favorite: true });
  await go(window, 'manage');
  await window.getByTestId('bank-gig').click();
  await window.getByTestId('slot-1').click();
  await window.getByTestId('slot-2').click({ modifiers: ['ControlOrMeta'] });
  await shoot(window, window.getByRole('main'), 'manage');
});
