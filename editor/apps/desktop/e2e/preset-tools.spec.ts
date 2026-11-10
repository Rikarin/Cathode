import type { Page } from '@playwright/test';
import { expect, test } from './fixtures';

// The Preset menu's tools and the dialogs that work across presets: templates, Revert, Clear, Batch Set, Leveling.

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
