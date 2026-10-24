import { expect, test } from './fixtures';

// Presets as a whole: saving and reloading them, A/B compare, and the editor's keyboard behaviour around them.

test('a saved preset reloads with its blocks, channels and values', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  await editor.segment('Channel', 'B').click();
  await editor.setKnob('Drive', '7');
  await editor.addBlock(2, 3, 'Delay');
  await editor.setKnob('Time', '420 ms');

  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: /Save as…/ }).click();
  await window.getByTestId('save-as-name').fill('Round Trip');
  await window.getByTestId('save-as-confirm').click();
  await expect(window.getByTestId('preset-name')).toHaveText('Round Trip');

  // A new preset starts empty.
  await window.getByRole('button', { name: 'New preset' }).click();
  await expect(editor.block('Drive', 'drive1')).toHaveCount(0);

  // Load it back from the library.
  await window.getByRole('link', { name: 'Library' }).click();
  await window.getByTestId('preset-list').getByText('Round Trip').click();
  await window.getByTestId('load-preset').click();
  await window.getByRole('link', { name: 'Editor' }).click();
  // Just loaded, so nothing is unsaved: no marker after the name.
  await expect(window.getByTestId('preset-name')).toHaveText('Round Trip');

  const drive = editor.block('Drive', 'drive1');
  await expect(drive).toHaveAccessibleName(/channel B/);
  await drive.click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '7');
  await editor.block('Delay', 'delay1').click();
  await expect(editor.knob('Time')).toHaveAttribute('aria-valuenow', '420');
});

test('A/B compare keeps two versions of the preset', async ({ editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  await editor.setKnob('Drive', '7');

  // B starts as a copy of A; edit it.
  await editor.segment('A/B compare', 'B').click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '7');
  await editor.setKnob('Drive', '2');
  await editor.addBlock(2, 3, 'Delay');

  // A is as it was, without the delay; B has both edits.
  await editor.segment('A/B compare', 'A').click();
  await expect(editor.block('Delay', 'delay1')).toHaveCount(0);
  await editor.block('Drive', 'drive1').click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '7');

  await editor.segment('A/B compare', 'B').click();
  await expect(editor.block('Delay', 'delay1')).toBeVisible();
  await editor.block('Drive', 'drive1').click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '2');
});

test('select-all selects nothing on the page but still works in text fields', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  await editor.grid.press('ControlOrMeta+a');
  expect(await window.evaluate(() => getSelection()?.toString() ?? '')).toBe('');

  await editor.cell(1, 2).dblclick();
  const search = window.getByRole('combobox', { name: 'Search blocks' });
  await search.fill('reverb');
  await search.press('ControlOrMeta+a');
  expect(await search.evaluate((input: HTMLInputElement) => [input.selectionStart, input.selectionEnd])).toEqual([
    0, 6
  ]);
});
