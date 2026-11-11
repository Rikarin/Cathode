import type { Page } from '@playwright/test';
import { expect, impulseWav, test } from './fixtures';

// The Library's tools: Manage presets' Reset Amp blocks, the IR list and the Block Library.

interface CathodeWindow {
  cathode: { connection(): Promise<{ baseUrl: string; token: string }> };
}

/** Saves the preset under a name through the Preset menu. */
async function saveAs(window: Page, name: string): Promise<void> {
  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: /Save as…/ }).click();
  await window.getByTestId('save-as-name').fill(name);
  await window.getByTestId('save-as-confirm').click();
  await expect(window.getByTestId('preset-name')).toHaveText(name);
}

test('Reset Amp blocks puts a stored preset’s expert amp settings back, and keeps the amp’s own panel', async ({
  window,
  editor
}) => {
  await editor.addBlock(2, 2, 'Amp');

  // One control of the amp's own panel, and one only the Ideal view shows (an expert setting), both moved.
  const view = window.getByTestId('amp-view');
  const knobs = window.locator('cth-param-control cth-knob [role=slider]');
  const names = () =>
    knobs.evaluateAll(all =>
      all.map(knob => document.getElementById(knob.getAttribute('aria-labelledby') ?? '')?.textContent.trim() ?? '')
    );
  await expect(view.getByRole('radio', { name: 'Authentic' })).toBeChecked();
  await expect(knobs.first()).toBeVisible();
  const panel = await names();
  await view.getByRole('radio', { name: 'Ideal' }).click();
  await expect.poll(async () => (await names()).length).toBeGreaterThan(panel.length);
  const expert = (await names()).find(name => !panel.includes(name));
  const front = panel[0];
  if (!expert || !front) {
    throw new Error('The Amp has no control outside its own panel to reset');
  }

  const turn = async (name: string) => {
    const knob = editor.knob(name);
    const before = await knob.getAttribute('aria-valuenow');
    await knob.focus();
    await knob.press((await knob.getAttribute('aria-valuemax')) === before ? 'Home' : 'End');
    await expect(knob).not.toHaveAttribute('aria-valuenow', before ?? '');
    return { before, turned: await knob.getAttribute('aria-valuenow') };
  };
  const expertValues = await turn(expert);
  const frontValues = await turn(front);
  await saveAs(window, 'Amp To Reset');

  // Manage presets works on bank slots: put the preset in a bank through the Server, as dragging it there would.
  await window.evaluate(async () => {
    const { baseUrl, token } = await (window as unknown as CathodeWindow).cathode.connection();
    const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
    const presets = (await (await fetch(`${baseUrl}/api/v1/presets`, { headers })).json()) as {
      id: string;
      name: string;
    }[];
    const id = presets.find(p => p.name === 'Amp To Reset')?.id;
    if (!id) {
      throw new Error('Amp To Reset was not saved');
    }

    await fetch(`${baseUrl}/api/v1/banks/gig`, {
      method: 'PUT',
      headers,
      body: JSON.stringify({ id: 'gig', name: 'Gig', slots: [id] })
    });
  });

  await window.getByTestId('preset-menu').click();
  await window.getByRole('menuitem', { name: 'Manage presets…' }).click();
  await expect(window).toHaveURL(/#\/manage/);
  const reset = window.getByTestId('reset-amps');
  const selection = window.getByRole('toolbar', { name: 'Selected presets' });
  await expect(reset).toBeDisabled(); // nothing selected

  // A factory preset is read-only: selecting one leaves the button off.
  await window.getByRole('list', { name: 'Banks' }).getByText('Factory').click();
  await window.getByTestId('slot-0').click();
  await expect(selection).toContainText('1 selected');
  await expect(reset).toBeDisabled();

  await window.getByTestId('bank-gig').click();
  await expect(window.getByTestId('slot-0')).toContainText('Amp To Reset');
  await window.getByTestId('slot-0').click();
  await expect(selection).toContainText('1 selected');
  await reset.click();

  // It asks first, saying how many presets it changes; Cancel changes nothing.
  const confirm = window.getByRole('alertdialog', { name: 'Reset Amp blocks?' });
  await expect(confirm).toContainText('in 1 preset(s)');
  await confirm.getByRole('button', { name: 'Cancel' }).click();
  await expect(confirm).toBeHidden();
  await reset.click();
  await confirm.getByRole('button').last().click();
  await expect(window.getByText('Reset the Amp blocks of 1 preset(s).')).toBeVisible();
  // It acted on the stored preset at once: there is nothing for Save to do.
  await expect(window.getByTestId('manage-dirty')).toHaveCount(0);

  // The loaded preset shows it: the expert setting is back, the amp's own knob is where it was put.
  await window.getByRole('button', { name: 'Close' }).click();
  await window.getByRole('link', { name: 'Editor' }).click();
  await editor.block('Amp', 'circuitamp1').click();
  await view.getByRole('radio', { name: 'Ideal' }).click();
  await expect(editor.knob(expert)).toHaveAttribute('aria-valuenow', expertValues.before ?? '');
  await expect(editor.knob(front)).toHaveAttribute('aria-valuenow', frontValues.turned ?? '');
  await expect(window.getByLabel('unsaved changes')).toHaveCount(0);
});

test('the IR list selects several IRs, marks favourites and deletes the selection in one go', async ({ window }) => {
  await window.getByRole('link', { name: 'Library' }).click();
  await window.getByRole('radio', { name: 'IR cabs' }).click();
  await window.getByTestId('library-upload-files').setInputFiles([
    { name: 'A close.wav', mimeType: 'audio/wav', buffer: impulseWav(150) },
    { name: 'A far.wav', mimeType: 'audio/wav', buffer: impulseWav(300) },
    { name: 'A room.wav', mimeType: 'audio/wav', buffer: impulseWav(600) }
  ]);
  const list = window.getByTestId('asset-list');
  // The user's own rows: the factory IRs ("Cathode ...", read-only) are always listed too, and sort after these names.
  const rows = list.locator('[data-testid^=asset-row-]').filter({ hasNot: window.getByTestId('factory-asset-tag') });
  await expect(rows).toHaveCount(3);

  // A favourite is listed alone under the Favourites filter.
  await list.getByRole('button', { name: 'Favourite A far.wav' }).click();
  await expect(list.getByRole('button', { name: 'Unfavourite A far.wav' })).toBeVisible();
  await window.getByRole('button', { name: 'Favourites', exact: true }).click();
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText('A far.wav');
  await window.getByRole('button', { name: 'Favourites', exact: true }).click();
  await expect(rows).toHaveCount(3);

  // Favourites are listed first. Click the first row, then ⇧-click the last: the row between is selected too.
  // ⌘-click takes one out again.
  await expect(rows.first()).toContainText('A far.wav');
  await list.getByText('A far.wav').click();
  await list.getByText('A room.wav').click({ modifiers: ['Shift'] });
  await expect(list.locator('[aria-selected=true]')).toHaveCount(3);
  await list.getByText('A far.wav').click({ modifiers: ['ControlOrMeta'] });
  await expect(list.locator('[aria-selected=true]')).toHaveCount(2);

  // Delete asks once for the two, and the favourite stays.
  await window.getByTestId('delete-assets').click();
  const confirm = window.getByRole('alertdialog');
  await expect(confirm).toContainText('2');
  await confirm.getByRole('button', { name: 'Delete' }).click();
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText('A far.wav');
});

test('a block’s channel or the whole block is saved to the Block Library and recalled', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Delay');
  await editor.setKnob('Mix', '80');
  await editor.segment('Channel', 'B').click();
  await editor.setKnob('Mix', '20');

  // Channel B alone, under a name.
  await window.keyboard.press('ControlOrMeta+b');
  const saving = window.getByRole('dialog', { name: /Save/ });
  await expect(
    saving.getByRole('radiogroup', { name: 'What to save' }).getByRole('radio', { name: 'Channel B' })
  ).toBeChecked();
  await window.getByTestId('block-preset-name').fill('Quiet echo');
  await window.getByTestId('block-preset-save').click();
  await expect(saving).toBeHidden();

  // The whole block, both channels as they are now.
  await window.keyboard.press('ControlOrMeta+b');
  await saving.getByRole('radio', { name: /^Whole block/ }).click();
  await window.getByTestId('block-preset-name').fill('Both echoes');
  await window.getByTestId('block-preset-save').click();
  await expect(saving).toBeHidden();

  // The Library lists both under the Delay.
  await window.getByRole('link', { name: 'Library' }).click();
  await window.getByRole('radio', { name: 'Block Library' }).click();
  const section = window.getByTestId('block-library-section');
  await expect(section.getByText('Quiet echo')).toBeVisible();
  await expect(section.getByText('Both echoes')).toBeVisible();
  await window.getByRole('link', { name: 'Editor' }).click();
  await editor.block('Delay', 'delay1').click();

  // Recalled into the channel in use: channel A takes the entry's Mix, channel B keeps its own.
  await editor.setKnob('Mix', '55');
  await editor.segment('Channel', 'A').click();
  const recall = window.getByTestId('block-library').getByRole('combobox');
  await recall.click();
  await window.getByRole('option', { name: 'Quiet echo (mine)', exact: true }).click();
  await expect(editor.knob('Mix')).toHaveAttribute('aria-valuenow', '20');
  await editor.segment('Channel', 'B').click();
  await expect(editor.knob('Mix')).toHaveAttribute('aria-valuenow', '55');

  // Into all channels: the whole-block entry brings back every channel as it was saved.
  await window.getByRole('switch', { name: 'Recall into all channels' }).click();
  await recall.click();
  await window.getByRole('option', { name: 'Both echoes (mine)', exact: true }).click();
  await expect(editor.knob('Mix')).toHaveAttribute('aria-valuenow', '20');
  await editor.segment('Channel', 'A').click();
  await expect(editor.knob('Mix')).toHaveAttribute('aria-valuenow', '80');
});
