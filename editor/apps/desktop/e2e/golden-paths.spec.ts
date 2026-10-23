import { type ElectronApplication, type Page, _electron as electron, expect, test } from '@playwright/test';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const main = resolve(__dirname, '../../../dist/apps/desktop/main.js');

let app: ElectronApplication;
let window: Page;
let scratch: string;

test.beforeEach(async () => {
  scratch = mkdtempSync(join(tmpdir(), 'cathode-e2e-'));
  app = await electron.launch({
    args: [main],
    env: {
      ...process.env,
      CATHODE_USER_DATA: join(scratch, 'user-data'),
      CATHODE_DATA_DIR: join(scratch, 'server-data')
    }
  });
  window = await app.firstWindow();
  // First start: the sidecars have to come up and the editor has to connect.
  await expect(window.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible({ timeout: 90_000 });
});

test.afterEach(async () => {
  await app?.close();
  rmSync(scratch, { recursive: true, force: true });
});

test('a guitarist builds a preset from scratch and saves it', async () => {
  // Drop a drive onto the shunt row, from the block palette.
  await window.getByTestId('cell-0-2').dblclick();
  await window.getByRole('combobox', { name: 'Search blocks' }).fill('Drive');
  await window.keyboard.press('Enter');
  const drive = window.getByRole('gridcell', { name: /^Drive drive1/ });
  await expect(drive).toBeVisible();

  // Its page opens; turn the first knob up with the keyboard.
  const knob = window.locator('cth-knob [role=slider]').first();
  const before = await knob.getAttribute('aria-valuenow');
  await knob.focus();
  await knob.press('PageUp');
  await expect(knob).not.toHaveAttribute('aria-valuenow', before ?? '');

  // Bypass it from the grid with B, and engage it again from the block page.
  await drive.click();
  await window.getByRole('grid', { name: 'Signal grid' }).press('b');
  await expect(drive).toHaveAccessibleName(/bypassed/);
  await window.getByTestId('engage').click();
  await expect(drive).not.toHaveAccessibleName(/bypassed/);

  // Save it under a name and find it in the library.
  await window.getByRole('button', { name: 'Save as…' }).click();
  await window.getByTestId('save-as-name').fill('First Rig');
  await window.getByTestId('save-as-confirm').click();
  await expect(window.getByTestId('preset-name')).toHaveText('First Rig');
  await window.getByRole('link', { name: 'Library' }).click();
  await expect(window.getByRole('grid').getByText('First Rig')).toBeVisible();
});

test('undo, redo and scenes change the rig', async () => {
  await window.getByTestId('cell-0-3').dblclick();
  await window.getByRole('combobox', { name: 'Search blocks' }).fill('Delay');
  await window.keyboard.press('Enter');
  const delay = window.getByRole('gridcell', { name: /^Delay delay1/ });
  await expect(delay).toBeVisible();

  await window.getByRole('button', { name: 'Undo' }).click();
  await expect(delay).toHaveCount(0);
  await window.getByRole('button', { name: 'Redo' }).click();
  await expect(delay).toBeVisible();

  // Bypass on scene 2 only: scene 1 keeps it engaged.
  await window.getByTestId('scenes').getByText('2', { exact: true }).click();
  await delay.click();
  await window.getByRole('grid', { name: 'Signal grid' }).press('b');
  await expect(delay).toHaveAccessibleName(/bypassed/);
  await window.getByTestId('scenes').getByText('1', { exact: true }).click();
  await expect(delay).not.toHaveAccessibleName(/bypassed/);
});

test('the analyser, its RTA and the tuner open and stream from the engine', async () => {
  await window.getByRole('button', { name: 'Analyser' }).click();
  await expect(window.getByRole('dialog', { name: 'Analyser' })).toBeVisible();
  await expect(window.getByRole('img', { name: 'Output waveform' })).toBeVisible();
  await window.getByRole('radio', { name: 'RTA' }).click();
  await expect(window.getByRole('img', { name: 'Output spectrum' })).toBeVisible();
  await window.keyboard.press('Escape');
  await window.getByRole('button', { name: 'Tuner' }).click();
  await expect(window.getByText('Play a string')).toBeVisible();
});

test('the IRs page mixes impulse responses', async () => {
  await window.getByRole('link', { name: 'IRs' }).click();
  await expect(window.getByRole('heading', { name: 'IR mixer' })).toBeVisible();
  await expect(window.getByTestId('mix-irs')).toBeDisabled(); // nothing picked yet
  await expect(window.getByRole('heading', { name: 'Capture an IR' })).toBeVisible(); // desktop only
  await window.getByRole('link', { name: 'Editor' }).click();
});

test('a knob gets a modifier, taps set the tempo, and the looper records', async () => {
  await window.getByTestId('cell-0-2').dblclick();
  await window.getByRole('combobox', { name: 'Search blocks' }).fill('Delay');
  await window.keyboard.press('Enter');
  await expect(window.getByRole('gridcell', { name: /^Delay delay1/ })).toBeVisible();

  // Right-click a knob: give it an LFO modifier.
  await window.getByRole('slider', { name: /^Mix/ }).click({ button: 'right' });
  await window.getByRole('menuitem', { name: 'Add modifier…' }).click();
  await window.getByTestId('modifier-save').click();
  await expect(window.getByLabel('Modified by lfo1')).toBeVisible();

  // Four taps half a second apart: about 120 BPM (HTTP and click timing add a little).
  for (let i = 0; i < 4; i++) {
    await window.getByTestId('tap').click();
    await window.waitForTimeout(500);
  }

  await expect(window.getByTestId('tempo')).toHaveText(/1[01]\d(\.\d)? BPM/);

  await window.getByRole('button', { name: 'Looper' }).click();
  await window.getByTestId('looper-record').click();
  await window.waitForTimeout(600);
  await window.getByTestId('looper-play').click();
  await expect(window.getByRole('dialog', { name: 'Looper' }).getByText('playing')).toBeVisible();
});

test('the global page holds tempo, EQ, MIDI and footswitches', async () => {
  await window.getByRole('link', { name: 'Global' }).click();
  await expect(window.getByRole('heading', { name: 'Global output EQ' })).toBeVisible();
  await window.getByRole('button', { name: 'New layout' }).click();
  await window.getByRole('button', { name: 'Add switch' }).click();
  await expect(window.getByLabel('Switch 1 tap action')).toHaveValue('scene 0');
});

test('the setup page shows the engine the desktop app started', async () => {
  await window.getByRole('link', { name: 'Setup' }).click();
  await expect(window.getByText('Backend')).toBeVisible();
  await expect(window.getByText('null', { exact: true })).toBeVisible();
  await expect(window.getByRole('heading', { name: 'Audio interface' })).toBeVisible();
});
