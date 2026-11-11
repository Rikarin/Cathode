import { expect, test } from './fixtures';

// The Amp and Cab blocks' page tools: the Authentic and Ideal views, the cab resonance helper and the Cab's Align view.

test('the Amp page shows the model’s own panel or every control, and the resonance helper sets Load resonance', async ({
  window,
  editor
}) => {
  await editor.addBlock(2, 2, 'Amp');
  await expect(editor.block('Amp', 'circuitamp1')).toBeVisible();

  // Authentic is the model's own panel; Ideal adds the controls the real amp lacks.
  const view = window.getByTestId('amp-view');
  await expect(view.getByRole('radio', { name: 'Authentic' })).toBeChecked();
  const knobs = window.locator('cth-knob [role=slider]');
  await expect(knobs.first()).toBeVisible();
  const authentic = await knobs.count();
  await view.getByRole('radio', { name: 'Ideal' }).click();
  await expect.poll(() => knobs.count()).toBeGreaterThan(authentic);
  await view.getByRole('radio', { name: 'Authentic' }).click();
  await expect(knobs).toHaveCount(authentic);

  // The resonance helper plays a test tone to sweep; applying it is an edit to the preset.
  await window.getByTestId('cab-resonance').click();
  const helper = window.getByRole('region', { name: 'Cab resonance helper' });
  await expect(helper).toBeVisible();
  const frequency = window.getByTestId('tone-frequency');
  const before = await frequency.textContent();
  await helper.getByRole('slider', { name: 'Test tone frequency' }).fill('800');
  await expect(frequency).not.toHaveText(before ?? '');
  await window.getByTestId('apply-resonance').click();
  await expect(helper).toBeHidden();
  await expect(window.getByTestId('cab-resonance')).toBeVisible();
  await expect(window.getByLabel('unsaved changes')).toBeVisible();

  // Opening it again and cancelling changes nothing more.
  await window.getByTestId('cab-resonance').click();
  await helper.getByRole('button', { name: 'Cancel' }).click();
  await expect(helper).toBeHidden();
});

test('the Cab’s Align view shows each mic’s onset, nudges its delay and lines the onsets up', async ({
  window,
  editor
}) => {
  await editor.addBlock(2, 2, 'Cab');
  await window.getByTestId('cab-view').getByRole('radio', { name: 'Align' }).click();
  const plot = window.getByRole('img', { name: 'Slot alignment' });
  /** A mic's onset as the plot labels it, in ms. */
  const onset = async (mic: number) =>
    new RegExp(`Mic ${mic} · (\\d+\\.\\d\\d) ms`).exec((await plot.textContent()) ?? '')?.[1] ?? null;
  await expect.poll(() => onset(4)).not.toBeNull();
  const before = await onset(1);

  // +0.1 moves the selected mic a tenth of a millisecond later: its Delay knob and its onset in the plot say so.
  const delay = editor.knob('Mic 1 Delay');
  await expect(delay).toHaveAttribute('aria-valuenow', '0');
  const tools = window.getByTestId('cab-align');
  await tools.getByRole('button', { name: '+0.1', exact: true }).click();
  await expect(delay).toHaveAttribute('aria-valuenow', '0.1');
  await expect.poll(() => onset(1)).toBe((Number(before) + 0.1).toFixed(2));
  await expect(window.getByLabel('unsaved changes')).toBeVisible();

  // -0.01 takes a hundredth back.
  await tools.getByRole('button', { name: '-0.01', exact: true }).click();
  await expect(delay).toHaveAttribute('aria-valuenow', '0.09');

  // A second mic, heard as well; Align onsets then gives both the same onset.
  const slots = window.getByRole('radiogroup', { name: 'Cab slot' });
  await slots.getByRole('radio', { name: /^Mic 2/ }).click();
  await window.getByRole('switch', { name: 'Mic 2 Mute' }).click();
  await expect(slots.getByRole('radio', { name: 'Mic 2', exact: true })).toBeVisible();
  await tools.getByRole('button', { name: 'Align onsets' }).click();
  await expect.poll(async () => (await onset(1)) === (await onset(2))).toBe(true);

  // The Response view is still there.
  await window.getByTestId('cab-view').getByRole('radio', { name: 'Response' }).click();
  await expect(tools).toHaveCount(0);
});
