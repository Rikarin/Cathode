import { expect, test } from './fixtures';

// The selected block's page: its type, knobs, bypass mode, channels A-D and how scenes recall them.

test('a block’s type, knobs and bypass mode are edited from its page', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Delay');
  await expect(window.getByRole('region', { name: 'Delay settings' })).toBeVisible();

  // Type: Digital to Tape.
  await editor.choose('Type', 'Tape');
  await expect(window.getByRole('combobox', { name: 'Type', exact: true })).toContainText('Tape');

  // Type a value with its unit, nudge it with the keyboard, ⌥-click back to the default.
  const time = editor.knob('Time');
  await editor.setKnob('Time', '500 ms');
  await expect(time).toHaveAttribute('aria-valuenow', '500');
  await time.press('PageUp');
  await expect(time).not.toHaveAttribute('aria-valuenow', '500');
  await time.click({ modifiers: ['Alt'] });
  await expect(time).toHaveAttribute('aria-valuenow', '375');

  // Bypass mode.
  await editor.choose('Bypass mode', 'Mute');
  await expect(window.getByRole('combobox', { name: 'Bypass mode' })).toContainText('Mute');

  // The page shows the same values after selecting another block and coming back.
  await editor.addBlock(2, 4, 'Drive');
  await editor.block('Delay', 'delay1').click();
  await expect(window.getByRole('combobox', { name: 'Type', exact: true })).toContainText('Tape');
  await expect(window.getByRole('combobox', { name: 'Bypass mode' })).toContainText('Mute');
});

test('each channel keeps its own settings, and one channel copies to another', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  const drive = editor.block('Drive', 'drive1');
  await expect(drive).toHaveAccessibleName(/channel A/);

  await editor.setKnob('Drive', '8');
  await editor.choose('Type', 'Fuzz');

  // Channel B starts from the defaults, and keeps its own edits.
  await editor.segment('Channel', 'B').click();
  await expect(drive).toHaveAccessibleName(/channel B/);
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '5');
  await expect(window.getByRole('combobox', { name: 'Type', exact: true })).toContainText('Overdrive');
  await editor.setKnob('Drive', '2');

  await editor.segment('Channel', 'A').click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '8');
  await expect(window.getByRole('combobox', { name: 'Type', exact: true })).toContainText('Fuzz');

  // Copy A to C from the block's menu.
  await window.getByRole('button', { name: 'Block actions' }).click();
  await window.getByRole('menuitem', { name: 'Channel C' }).first().click(); // Copy to; Swap with comes second
  await editor.segment('Channel', 'C').click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '8');
  await expect(window.getByRole('combobox', { name: 'Type', exact: true })).toContainText('Fuzz');

  // B was left alone.
  await editor.segment('Channel', 'B').click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '2');
});

test('each scene recalls the channel and bypass state of every block', async ({ editor }) => {
  await editor.addBlock(2, 2, 'Drive');
  await editor.addBlock(2, 3, 'Delay');
  const drive = editor.block('Drive', 'drive1');
  const delay = editor.block('Delay', 'delay1');

  // Scene 2: the drive on channel B with more gain, the delay bypassed.
  await editor.segment('Scene', '2').click();
  await drive.click();
  await editor.segment('Channel', 'B').click();
  await editor.setKnob('Drive', '9');
  await delay.click();
  await editor.grid.press('Space');
  await expect(drive).toHaveAccessibleName(/channel B/);
  await expect(delay).toHaveAccessibleName(/bypassed/);

  // Scene 1 is untouched.
  await editor.segment('Scene', '1').click();
  await expect(drive).toHaveAccessibleName(/channel A/);
  await expect(delay).not.toHaveAccessibleName(/bypassed/);
  await drive.click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '5');

  // And scene 2 comes back as it was left.
  await editor.segment('Scene', '2').click();
  await expect(drive).toHaveAccessibleName(/channel B/);
  await expect(delay).toHaveAccessibleName(/bypassed/);
  await drive.click();
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '9');
});
