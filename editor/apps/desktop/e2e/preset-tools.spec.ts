import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';

// The Preset menu's tools and the dialogs that work across presets: templates, Revert, Clear, Batch Set, Leveling,
// Recent presets.

/** Saves the preset under a name through the Preset menu. */
async function saveAs(window: Page, name: string): Promise<void> {
  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: /Save as…/ }).click();
  await window.getByTestId('save-as-name').fill(name);
  await window.getByTestId('save-as-confirm').click();
  await expect(window.getByTestId('preset-name')).toHaveText(name);
}

/** Loads a preset from the Library and returns to the editor. */
async function load(window: Page, name: string): Promise<void> {
  await window.getByRole('link', { name: 'Library' }).click();
  // The list draws only the rows in view: search, so the preset is one of them.
  await window.getByRole('searchbox', { name: 'Search presets' }).fill(name);
  await window.getByTestId('preset-list').getByText(name, { exact: true }).click();
  await window.getByTestId('load-preset').click();
  await window.getByRole('link', { name: 'Editor' }).click();
  await expect(window.getByTestId('preset-name')).toHaveText(name);
}

test('a rig is saved as a template, a new preset starts from it, and the template is deleted', async ({
  window,
  editor
}) => {
  await editor.addBlock(2, 2, 'Drive');
  await editor.addBlock(2, 3, 'Delay');
  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'Save as template…' }).click();
  const saving = window.getByRole('dialog', { name: 'Save as template' });
  await saving.getByRole('textbox', { name: 'Template name' }).fill('Pedal start');
  await saving.getByRole('button', { name: 'Save' }).click();
  await expect(saving).toBeHidden();

  // Empty the grid, then start again from the template.
  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'Clear preset…' }).click();
  await window.getByRole('alertdialog').getByRole('button', { name: 'Clear' }).click();
  await expect(editor.block('Drive', 'drive1')).toHaveCount(0);

  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'New from template…' }).click();
  const templates = window.getByRole('dialog', { name: 'New from template' });
  const row = templates.getByTestId('template-row').filter({ hasText: 'Pedal start' });

  // The cleared preset has unsaved edits, so starting from the template asks first. Cancel: nothing changes.
  const asked: string[] = [];
  window.once('dialog', dialog => {
    asked.push(dialog.message());
    void dialog.dismiss().catch(() => undefined);
  });
  await row.getByRole('button', { name: 'New' }).click();
  await expect.poll(() => asked).toHaveLength(1);
  expect(asked[0]).toContain('start from “Pedal start”');
  await expect(templates).toBeVisible();

  // OK: the template's rig is on the grid.
  window.once('dialog', dialog => void dialog.accept().catch(() => undefined));
  await row.getByRole('button', { name: 'New' }).click();
  await expect(templates).toBeHidden();
  await expect(editor.block('Drive', 'drive1')).toBeVisible();
  await expect(editor.block('Delay', 'delay1')).toBeVisible();

  // Deleting the template asks first; cancelling keeps it.
  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'New from template…' }).click();
  const confirming = window.getByRole('alertdialog', { name: 'Delete template?' });
  await templates.getByRole('button', { name: 'Delete template Pedal start' }).click();
  await expect(confirming).toContainText('“Pedal start” will be deleted');
  await confirming.getByRole('button', { name: 'Cancel' }).click();
  await expect(confirming).toBeHidden();
  await expect(row).toBeVisible();

  // Delete: the list is empty again.
  await templates.getByRole('button', { name: 'Delete template Pedal start' }).click();
  await confirming.getByRole('button', { name: 'Delete' }).click();
  await expect(templates.getByText('No templates yet')).toBeVisible();
});

test('Revert goes back to the saved preset, and Clear empties it until Undo', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  await saveAs(window, 'Base');

  // An unsaved edit: a second block and a knob turned.
  await editor.addBlock(2, 3, 'Delay');
  await editor.setKnob('Mix', '80');
  await expect(window.getByLabel('unsaved changes')).toBeVisible();

  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'Revert to saved' }).click();
  await expect(editor.block('Delay', 'delay1')).toHaveCount(0);
  await expect(editor.block('Drive', 'drive1')).toBeVisible();
  await expect(window.getByLabel('unsaved changes')).toHaveCount(0);

  // With nothing to revert the item is off.
  await window.getByTestId('preset-menu').click();
  await expect(window.getByRole('menuitem', { name: 'Revert to saved' })).toBeDisabled();
  await window.keyboard.press('Escape');

  // Clear asks first; cancelling keeps the rig.
  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'Clear preset…' }).click();
  await window.getByRole('alertdialog').getByRole('button', { name: 'Cancel' }).click();
  await expect(editor.block('Drive', 'drive1')).toBeVisible();

  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'Clear preset…' }).click();
  await window.getByRole('alertdialog').getByRole('button', { name: 'Clear' }).click();
  await expect(editor.block('Drive', 'drive1')).toHaveCount(0);

  // Undo brings the rig back.
  await window.getByRole('button', { name: 'Undo' }).click();
  await expect(editor.block('Drive', 'drive1')).toBeVisible();
});

test('Batch Set puts a knob’s value into the same block of another preset', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Delay');
  await saveAs(window, 'Batch Clean');
  const original = await editor.knob('Mix').getAttribute('aria-valuenow');
  await saveAs(window, 'Batch Lead');
  await editor.block('Delay', 'delay1').click();
  await editor.setKnob('Mix', '80');
  await expect(editor.knob('Mix')).toHaveAttribute('aria-valuenow', '80');
  expect(original).not.toBe('80');

  await editor.knob('Mix').click({ button: 'right' });
  await window.getByRole('menuitem', { name: 'Set in other presets…' }).click();
  const dialog = window.getByRole('dialog', { name: 'Set in other presets' });
  await expect(dialog).toContainText('delay1');
  await expect(dialog.getByRole('button', { name: 'Set', exact: true })).toBeDisabled();
  await dialog.getByRole('searchbox', { name: 'Search presets' }).fill('Batch');
  await dialog.getByRole('checkbox', { name: 'Batch Clean', exact: true }).check();
  await expect(dialog.getByText('1 selected')).toBeVisible();
  await dialog.getByRole('button', { name: 'Set', exact: true }).click();
  await expect(dialog).toBeHidden();

  // The other preset has the value now.
  await window.keyboard.press('ControlOrMeta+s');
  await expect(window.getByLabel('unsaved changes')).toHaveCount(0);
  await load(window, 'Batch Clean');
  await editor.block('Delay', 'delay1').click();
  await expect(editor.knob('Mix')).toHaveAttribute('aria-valuenow', '80');
});

test('Preset Leveling measures, and offers no level until it has heard the preset', async ({ window }) => {
  await window.getByRole('button', { name: 'Preset Leveling' }).click();
  const dialog = window.getByRole('dialog', { name: 'Preset Leveling' });
  await expect(dialog.getByRole('radiogroup', { name: 'Target loudness' })).toBeVisible();
  await expect(dialog.getByTestId('leveling-apply')).toBeDisabled();

  // Measuring runs until it is stopped. The null audio backend plays silence, so there is nothing to apply.
  await dialog.getByRole('button', { name: 'Measure' }).click();
  await expect(dialog.getByRole('button', { name: 'Stop' })).toBeVisible();
  await expect(dialog.getByTestId('leveling-apply')).toBeDisabled();
  await dialog.getByRole('button', { name: 'Stop' }).click();
  await expect(dialog.getByRole('button', { name: 'Measure' })).toBeVisible();
  await expect(dialog.getByTestId('leveling-suggestion')).toHaveCount(0);
  await expect(dialog.getByTestId('leveling-apply')).toBeDisabled();
  await window.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
});

test('Preset Leveling measures a steady tone and applies the level that brings it to the target', async ({
  window,
  editor
}) => {
  // The null audio backend plays silence, so a Synth voice that drones without an input stands in for the playing.
  await editor.addBlock(2, 2, 'Synth');
  await editor.choose('Voice 1 Track', 'Off');

  await window.getByRole('button', { name: 'Preset Leveling' }).click();
  const dialog = window.getByRole('dialog', { name: 'Preset Leveling' });
  await dialog.getByRole('button', { name: 'Measure' }).click();
  // The loudness meter needs a moment of signal before it has a reading.
  await expect(dialog.getByTestId('lufs-measured')).toHaveText(/-\d+(\.\d+)?/, { timeout: 30_000 });
  await dialog.getByRole('button', { name: 'Stop' }).click();

  // The suggestion says what changes and by how much; another target suggests another level.
  const suggestion = dialog.getByTestId('leveling-suggestion');
  await expect(suggestion).toHaveText(/: [+-]?\d+(\.\d+)? dB, to -?\d+(\.\d+)? dB/);
  const atDefault = await suggestion.textContent();
  await dialog.getByRole('radiogroup', { name: 'Target loudness' }).getByRole('radio', { name: '-23' }).click();
  await expect(suggestion).not.toHaveText(atDefault ?? '');

  // Applying sets that level, and the measurement is used up.
  await dialog.getByTestId('leveling-apply').click();
  await expect(suggestion).toHaveCount(0);
  await expect(dialog.getByTestId('leveling-apply')).toBeDisabled();
  await window.keyboard.press('Escape');
  await expect(dialog).toBeHidden();

  // The level is an edit like any other: Undo takes it back and leaves the Synth as it was set.
  await window.getByRole('button', { name: 'Undo' }).click();
  await editor.block('Synth', 'synth1').click();
  await expect(window.getByRole('combobox', { name: 'Voice 1 Track', exact: true })).toContainText('Off');
});

test('Recent presets lists what was loaded, newest first, and loads one again', async ({ window }) => {
  await window.getByTestId('recent-presets').click();
  await expect(window.getByRole('menuitem', { name: 'No recent presets' })).toBeDisabled();
  await window.keyboard.press('Escape');

  await load(window, 'Apache');
  await load(window, 'Back in Black');
  await load(window, 'Basket Case');

  await window.getByTestId('recent-presets').click();
  const recent = window.getByRole('menu').getByRole('menuitem');
  await expect(recent).toHaveText(['Basket Case', 'Back in Black', 'Apache']);

  // Loading one from the list puts it at the top, once.
  await recent.filter({ hasText: 'Apache' }).click();
  await expect(window.getByTestId('preset-name')).toHaveText('Apache');
  await window.getByTestId('recent-presets').click();
  await expect(recent).toHaveText(['Apache', 'Basket Case', 'Back in Black']);
  await window.keyboard.press('Escape');

  // The list is this editor's own, kept across a reload.
  await window.reload();
  await window.getByTestId('recent-presets').click();
  await expect(window.getByRole('menu').getByRole('menuitem')).toHaveText(['Apache', 'Basket Case', 'Back in Black']);
});

test('the preset picker searches from the keyboard, stays open when pinned, and keeps favourites', async ({
  window,
  editor
}) => {
  // P opens it with the search focused: type, and Enter loads the first match and closes it.
  await editor.grid.press('p');
  const picker = window.getByRole('dialog', { name: 'Presets' });
  const search = picker.getByRole('combobox', { name: 'Search presets' });
  await expect(search).toBeFocused();
  await search.fill('apache');
  await expect(picker.getByRole('option')).toHaveCount(1);
  await search.press('Enter');
  await expect(picker).toBeHidden();
  await expect(window.getByTestId('preset-name')).toHaveText('Apache');

  // Pinned, it stays open to audition one preset after another; the loaded one is marked.
  await editor.grid.press('p');
  await picker.getByTestId('picker-pin').click();
  await search.fill('back in');
  await search.press('Enter');
  await expect(window.getByTestId('preset-name')).toHaveText('Back in Black');
  await expect(picker).toBeVisible();
  await expect(picker.getByRole('option', { name: /Back in Black/ }).getByLabel('loaded')).toBeVisible();

  // The arrow keys move the highlight, and the star under the list makes the highlighted preset a favourite.
  await search.fill('b');
  await search.press('ArrowDown');
  await expect(picker.locator('[role=option][aria-selected=true]')).toHaveCount(1);
  const star = picker.getByTestId('picker-favorite');
  await expect(star).toHaveAccessibleName(/^Favourite ./);
  const favourite = ((await star.getAttribute('aria-label')) ?? '').replace(/^Favourite /, '');
  await star.click();

  // Favourites only lists it alone, also with the search cleared; favourites come first in the whole list.
  await search.fill('');
  await picker.getByRole('button', { name: 'Favourites only' }).click();
  await expect(picker.getByRole('option')).toHaveCount(1);
  await expect(picker.getByRole('option')).toContainText(favourite);
  await picker.getByRole('button', { name: 'Favourites only' }).click();
  await expect(picker.getByRole('option').first()).toContainText(favourite);

  // A number is a slot of the bank chosen above the list: Enter loads it and closes the picker, pinned or not.
  await search.fill('1');
  await expect(picker.getByText(/^\s*Factory, slot 1: /)).toBeVisible();
  await search.press('Enter');
  await expect(picker).toBeHidden();

  // The pin lasts from one opening to the next: a preset loads and the picker stays.
  await editor.grid.press('p');
  await search.fill('apache');
  await search.press('Enter');
  await expect(window.getByTestId('preset-name')).toHaveText('Apache');
  await expect(picker).toBeVisible();
  await window.keyboard.press('Escape');
  await expect(picker).toBeHidden();
});
