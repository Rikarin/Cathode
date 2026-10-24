import { expect, test } from './fixtures';

// The editor workflow: keyboard shortcuts and their help, block commands, snapshots and preferences.

test('? lists the shortcuts, and the block shortcuts bypass, replace, cut and paste', async ({ window, editor }) => {
  await editor.grid.press('?');
  const help = window.getByRole('dialog', { name: 'Settings' });
  await expect(help.getByRole('tab', { name: 'Keyboard shortcuts' })).toHaveAttribute('aria-selected', 'true');
  await expect(help.getByRole('region', { name: 'Blocks' })).toContainText('Bypass in all scenes');
  await expect(help.getByRole('region', { name: 'Presets' })).toContainText('Open the preset picker');
  await window.keyboard.press('Escape');
  await expect(help).toBeHidden();

  await editor.addBlock(2, 2, 'Drive');
  const drive = editor.block('Drive', 'drive1');
  await drive.click();
  await editor.grid.press('Space');
  await expect(drive).toHaveAccessibleName(/bypassed/);
  await editor.grid.press('Shift+Space'); // engage in every scene
  await expect(drive).not.toHaveAccessibleName(/bypassed/);

  // Q on a block replaces it, on its cables.
  await editor.grid.press('q');
  const picker = window.getByRole('dialog', { name: /^Replace Drive with/ });
  await picker.getByRole('combobox', { name: 'Search blocks' }).fill('Delay');
  await window.keyboard.press('Enter');
  const delay = editor.block('Delay', 'delay1');
  await expect(delay).toHaveAccessibleName(/row 1, column 3$/);
  await expect(drive).toHaveCount(0);

  // ⌘X cuts it (a shunt stays), ⌘V puts it back.
  await delay.click();
  await editor.grid.press('ControlOrMeta+x');
  await expect(delay).toHaveCount(0);
  await expect(editor.cell(2, 2)).toHaveAccessibleName(/^Shunt/);
  await editor.grid.press('ControlOrMeta+v');
  await expect(editor.cell(2, 2)).toHaveAccessibleName(/^Delay/);
});

test('a snapshot keeps unsaved edits and loads back', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  await window.getByTestId('preset-menu').click();
  await window.getByTestId('take-snapshot').click();
  await expect(window.getByText(/Snapshot “.*” taken/)).toBeVisible();

  await editor.block('Drive', 'drive1').click();
  await editor.grid.press('Delete');
  await expect(editor.block('Drive', 'drive1')).toHaveCount(0);

  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'Snapshots…' }).click();
  await window.getByRole('dialog', { name: 'Snapshots' }).getByRole('button', { name: 'Load' }).click();
  await expect(editor.block('Drive', 'drive1')).toBeVisible();
});

test('preferences are kept and Block Info shows abbreviations', async ({ window, editor }) => {
  await window.keyboard.press('ControlOrMeta+Comma');
  await expect(
    window.getByRole('dialog', { name: 'Settings' }).getByRole('tab', { name: 'Preferences' })
  ).toHaveAttribute('aria-selected', 'true');
  await editor.segment('Mouse wheel', 'Off').click();
  await window.keyboard.press('Escape');

  await editor.addBlock(2, 2, 'Drive');
  const knob = editor.knob('Drive');
  const before = await knob.getAttribute('aria-valuenow');
  await knob.hover();
  await window.mouse.wheel(0, -200);
  await expect(knob).toHaveAttribute('aria-valuenow', before ?? '');

  await editor.grid.press('F1');
  await expect(window.getByTestId('info-drive1')).toHaveText('DRV');
  await expect(window.getByTestId('amps-in-use')).toBeVisible();
});
