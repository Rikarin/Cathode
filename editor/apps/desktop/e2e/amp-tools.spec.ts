import { expect, test } from './fixtures';

// The Amp block's page tools: the Authentic and Ideal views, and the cab resonance helper.

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
