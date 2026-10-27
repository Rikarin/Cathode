import { expect, test } from './fixtures';

test('a guitarist builds a preset from scratch and saves it', async ({ window }) => {
  // Drop a drive onto the shunt row, from the block palette.
  await window.getByTestId('cell-2-2').dblclick();
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

  // Bypass it from the grid with Space, and engage it again from the block page.
  await drive.click();
  await window.getByRole('grid', { name: 'Signal grid' }).press('Space');
  await expect(drive).toHaveAccessibleName(/bypassed/);
  await window.getByTestId('engage').click();
  await expect(drive).not.toHaveAccessibleName(/bypassed/);

  // Save it under a name and find it in the library.
  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: /Save as…/ }).click();
  await window.getByTestId('save-as-name').fill('First Rig');
  await window.getByTestId('save-as-confirm').click();
  await expect(window.getByTestId('preset-name')).toHaveText('First Rig');
  await window.getByRole('link', { name: 'Library' }).click();
  await expect(window.getByTestId('preset-list').getByText('First Rig')).toBeVisible();
});

test('undo, redo and scenes change the rig', async ({ window }) => {
  await window.getByTestId('cell-2-3').dblclick();
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
  await window.getByRole('grid', { name: 'Signal grid' }).press('Space');
  await expect(delay).toHaveAccessibleName(/bypassed/);
  await window.getByTestId('scenes').getByText('1', { exact: true }).click();
  await expect(delay).not.toHaveAccessibleName(/bypassed/);
});

test('the analyser, its RTA and the tuner open and stream from the engine', async ({ window }) => {
  await window.getByRole('button', { name: 'Analyser' }).click();
  await expect(window.getByRole('dialog', { name: 'Analyser' })).toBeVisible();
  await expect(window.getByRole('img', { name: 'Output waveform' })).toBeVisible();
  await window.getByRole('radio', { name: 'RTA' }).click();
  await expect(window.getByRole('img', { name: 'Output spectrum' })).toBeVisible();
  await window.keyboard.press('Escape');
  await window.getByRole('button', { name: 'Tuner' }).click();
  await expect(window.getByText('Play a string')).toBeVisible();
});

test('the Library holds the cab IRs and the IR tools', async ({ window }) => {
  await window.getByRole('link', { name: 'Library' }).click();
  await window.getByRole('radio', { name: 'IR cabs' }).click();
  await window.getByTestId('library-upload-files').setInputFiles({
    name: 'Room mic.wav',
    mimeType: 'audio/wav',
    buffer: impulseWav()
  });
  const list = window.getByTestId('asset-list');
  await expect(list.getByText('Room mic.wav')).toBeVisible();

  await list.getByText('Room mic.wav').dblclick();
  await window.getByTestId('asset-name').fill('Room mic');
  await window.keyboard.press('Enter');
  await expect(list.getByText('Room mic', { exact: true })).toBeVisible();

  await window.getByRole('button', { name: 'IR tools' }).click();
  await window.getByTestId('open-ir-mixer').click();
  await expect(window.getByTestId('mix-irs')).toBeDisabled(); // nothing picked yet
  await window.keyboard.press('Escape');
  await window.getByRole('button', { name: 'IR tools' }).click();
  await expect(window.getByTestId('open-ir-capture')).toBeVisible(); // desktop only
  await window.keyboard.press('Escape');

  await list.getByText('Room mic', { exact: true }).click();
  await window.getByTestId('delete-assets').click();
  await window.getByRole('alertdialog').getByRole('button', { name: 'Delete' }).click();
  await expect(window.getByText('No cab IRs yet')).toBeVisible();
  await window.getByRole('link', { name: 'Editor' }).click();
});

test('reloading the window brings the editor back on the same page', async ({ window }) => {
  await window.getByRole('link', { name: 'Library' }).click();
  await expect(window).toHaveURL(/#\/library$/);
  await window.reload();
  await expect(window.getByRole('heading', { name: 'Library' })).toBeVisible();
  // A chunk the build no longer has is not found, rather than answered with the app shell.
  expect(await window.evaluate(() => fetch('/chunk-gone.js').then(response => response.status))).toBe(404);
});

test('a knob gets a modifier, taps set the tempo, and the looper records', async ({ window }) => {
  await window.getByTestId('cell-2-2').dblclick();
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

test('the global page holds tempo, EQ, MIDI and footswitches', async ({ window }) => {
  await window.getByRole('link', { name: 'Global' }).click();
  await expect(window.getByRole('heading', { name: 'Outputs' })).toBeVisible();
  await window.getByRole('button', { name: 'New layout' }).click();
  await window.getByRole('button', { name: 'Add switch' }).click();
  await expect(window.getByLabel('Switch 1 tap action')).toContainText('scene');
  await expect(window.getByLabel('Switch 1 tap value')).toContainText('Scene 1');
});

test('the audio setup shows the engine the desktop app started', async ({ window }) => {
  await window.keyboard.press('ControlOrMeta+Comma');
  await window.getByRole('tab', { name: 'Audio setup' }).click();
  await expect(window.getByText('Backend')).toBeVisible();
  await expect(window.getByText('null', { exact: true })).toBeVisible();
  await expect(window.getByRole('heading', { name: 'Audio interface' })).toBeVisible();
});

/** A 48 kHz mono 16-bit WAV holding a decaying impulse: enough for the asset store to take as an IR. */
function impulseWav(): Buffer {
  const samples = 2048;
  const wav = Buffer.alloc(44 + samples * 2);
  wav.write('RIFF', 0);
  wav.writeUInt32LE(36 + samples * 2, 4);
  wav.write('WAVEfmt ', 8);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20); // PCM
  wav.writeUInt16LE(1, 22); // mono
  wav.writeUInt32LE(48_000, 24);
  wav.writeUInt32LE(96_000, 28);
  wav.writeUInt16LE(2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write('data', 36);
  wav.writeUInt32LE(samples * 2, 40);
  for (let i = 0; i < samples; i++) {
    wav.writeInt16LE(Math.round(32_000 * Math.exp(-i / 200) * Math.cos(i / 3)), 44 + i * 2);
  }

  return wav;
}
