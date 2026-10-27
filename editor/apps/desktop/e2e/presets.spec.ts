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

test('New preset asks before discarding unsaved edits, as loading does', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  const asked: string[] = [];

  // Cancel: the edits stay.
  window.once('dialog', dialog => {
    asked.push(dialog.message());
    void dialog.dismiss().catch(() => undefined);
  });
  await window.getByRole('button', { name: 'New preset', exact: true }).click();
  await expect.poll(() => asked).toHaveLength(1);
  expect(asked[0]).toMatch(/has unsaved changes\. Discard them and start a new preset\?$/);
  await expect(editor.block('Drive', 'drive1')).toBeVisible();

  // OK: an empty preset.
  window.once('dialog', dialog => void dialog.accept().catch(() => undefined));
  await window.getByRole('button', { name: 'New preset', exact: true }).click();
  await expect(editor.block('Drive', 'drive1')).toHaveCount(0);
});

test('quitting asks before discarding unsaved edits', async ({ app, window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  const asked: string[] = [];

  // Cancel: the app stays, edits and all.
  window.once('dialog', dialog => {
    asked.push(dialog.message());
    void dialog.dismiss().catch(() => undefined);
  });
  await app.evaluate(({ app }) => app.quit());
  await expect.poll(() => asked).toHaveLength(1);
  expect(asked[0]).toMatch(/has unsaved changes\. Discard them and quit\?$/);
  await expect(editor.block('Drive', 'drive1')).toBeVisible();

  // Closing the window quits too, except on macOS: it asks the same.
  if (process.platform !== 'darwin') {
    window.once('dialog', dialog => {
      asked.push(dialog.message());
      void dialog.dismiss().catch(() => undefined);
    });
    await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.close());
    await expect.poll(() => asked).toHaveLength(2);
    await expect(editor.block('Drive', 'drive1')).toBeVisible();
  }

  // OK: it quits.
  window.once('dialog', dialog => void dialog.accept());
  const closed = app.waitForEvent('close');
  await app.evaluate(({ app }) => app.quit());
  await closed;
});

test('⌘S on a new preset asks for its name, then saves it in place', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  await editor.grid.press('ControlOrMeta+s');
  const dialog = window.getByRole('dialog', { name: 'Save preset' });
  await expect(dialog.getByText('This preset has not been saved yet')).toBeVisible();
  await expect(window.getByTestId('save-as-name')).toHaveValue('New Preset');
  await window.getByTestId('save-as-name').fill('My Crunch Rig');
  await window.getByTestId('save-as-confirm').click();
  await expect(window.getByTestId('preset-name')).toHaveText('My Crunch Rig');

  // Now stored, ⌘S saves without asking.
  await editor.setKnob('Drive', '6');
  await editor.grid.press('ControlOrMeta+s');
  await expect(window.getByTestId('preset-name')).toHaveText('My Crunch Rig');
  await expect(dialog).toBeHidden();
  await window.getByRole('link', { name: 'Library' }).click();
  await expect(window.getByTestId('preset-list').getByText('My Crunch Rig', { exact: true })).toHaveCount(1);
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

test('a preset a bank holds is not deleted: the Library says which bank, then deletes it once it is free', async ({
  window
}) => {
  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: /Save as…/ }).click();
  await window.getByTestId('save-as-name').fill('Gig Rig');
  await window.getByTestId('save-as-confirm').click();
  await expect(window.getByTestId('preset-name')).toHaveText('Gig Rig');

  // Put it in slot 2 of a bank through the Server, as Manage presets would.
  await window.evaluate(async () => {
    const { baseUrl, token } = await (window as unknown as CathodeWindow).cathode.connection();
    const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
    const response = await fetch(`${baseUrl}/api/v1/presets`, { headers });
    const presets = (await response.json()) as { id: string; name: string }[];
    const id = presets.find(p => p.name === 'Gig Rig')?.id;
    if (!id) {
      throw new Error('Gig Rig was not saved');
    }

    await fetch(`${baseUrl}/api/v1/banks/gig`, {
      method: 'PUT',
      headers,
      body: JSON.stringify({ id: 'gig', name: 'Gig', slots: [null, id] })
    });
  });

  await window.getByRole('link', { name: 'Library' }).click();
  await window.getByTestId('preset-list').getByText('Gig Rig').click();
  await window.getByRole('button', { name: 'Delete', exact: true }).click();
  const inUse = window.getByRole('dialog', { name: 'This preset is in use' });
  await expect(inUse.getByTestId('preset-uses')).toHaveText('bank “Gig”, slot 2');
  await inUse.getByRole('button', { name: 'OK' }).click();
  await expect(window.getByTestId('preset-list').getByText('Gig Rig')).toBeVisible();

  // Out of the bank, it can go.
  await window.evaluate(async () => {
    const { baseUrl, token } = await (window as unknown as CathodeWindow).cathode.connection();
    await fetch(`${baseUrl}/api/v1/banks/gig`, { method: 'DELETE', headers: { Authorization: `Bearer ${token}` } });
  });
  await window.getByRole('button', { name: 'Delete', exact: true }).click();
  await window.getByRole('alertdialog', { name: 'Delete preset?' }).getByRole('button', { name: 'Delete' }).click();
  await expect(window.getByTestId('preset-list').getByText('Gig Rig')).toHaveCount(0);
});

interface CathodeWindow {
  cathode: { connection(): Promise<{ baseUrl: string; token: string | null }> };
}
