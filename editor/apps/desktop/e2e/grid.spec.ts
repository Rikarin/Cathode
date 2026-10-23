import { expect, test } from './fixtures';

// Building a signal chain on the grid: the block picker, moving, copying and removing blocks, and cabling.

test('the picker groups blocks by category, filters them and adds one from the keyboard', async ({
  window,
  editor
}) => {
  // Arrow to an empty cell on the shunt row and open the picker with Enter.
  await editor.cell(0, 1).click();
  await editor.grid.press('ArrowRight');
  await editor.grid.press('Enter');
  const picker = window.getByRole('dialog', { name: 'Add a block' });
  await expect(picker).toBeVisible();
  const list = picker.getByRole('listbox', { name: 'Blocks' });
  for (const category of ['Drive', 'Amp', 'Delay', 'Reverb']) {
    await expect(list.getByRole('group', { name: category })).toBeVisible();
  }

  // Search narrows it down; Escape leaves the grid as it was.
  const search = picker.getByRole('combobox', { name: 'Search blocks' });
  await search.fill('verb');
  await expect(list.getByRole('option', { name: 'Reverb' })).toBeVisible();
  await expect(list.getByRole('option', { name: 'Drive' })).toHaveCount(0);
  await search.press('Escape');
  await expect(picker).toBeHidden();
  await expect(editor.cell(0, 2)).toHaveAccessibleName(/^Shunt/);

  // Reopen, search by category, move with the arrows and add the highlighted block.
  await editor.grid.press('Enter');
  await search.fill('delay');
  await expect(search).toHaveAttribute('aria-activedescendant', 'block-option-delay');
  await search.press('ArrowRight');
  await expect(search).not.toHaveAttribute('aria-activedescendant', 'block-option-delay');
  await search.press('ArrowLeft');
  await expect(search).toHaveAttribute('aria-activedescendant', 'block-option-delay');
  await search.press('Enter');
  await expect(editor.block('Delay', 'delay1')).toHaveAccessibleName(/row 1, column 3$/);
});

test('blocks are copied, pasted, moved and removed, and undo brings them back', async ({ window, editor }) => {
  await editor.addBlock(0, 2, 'Drive');
  await editor.setKnob('Drive', '8');
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '8');

  // ⌘C on the drive, ⌘V on the row below: a second drive with the same settings.
  await editor.block('Drive', 'drive1').click();
  await editor.grid.press('ControlOrMeta+c');
  await editor.grid.press('ArrowDown');
  await editor.grid.press('ControlOrMeta+v');
  const copy = editor.block('Drive', 'drive2');
  await expect(copy).toHaveAccessibleName(/row 2, column 3$/);
  await expect(editor.knob('Drive')).toHaveAttribute('aria-valuenow', '8');

  // Drag the copy two columns to the right.
  await copy.dragTo(editor.cell(1, 4));
  await expect(copy).toHaveAccessibleName(/row 2, column 5$/);

  // Delete removes it; undo restores it where it was.
  await copy.click();
  await editor.grid.press('Delete');
  await expect(copy).toHaveCount(0);
  await window.getByRole('button', { name: 'Undo' }).click();
  await expect(copy).toHaveAccessibleName(/row 2, column 5$/);
});

test('cables connect blocks in neighbouring columns and are removed with Delete', async ({ window, editor }) => {
  await editor.addBlock(1, 3, 'Drive');
  await editor.addBlock(1, 4, 'Delay');
  const cable = window.getByTestId('cable-drive1>delay1');
  await expect(cable).toHaveCount(0);

  // Drag from the drive's outlet onto the delay.
  const outlet = await window.getByTestId('outlet-drive1').boundingBox();
  const target = await editor.cell(1, 4).boundingBox();
  if (!outlet || !target) {
    throw new Error('The outlet or the target cell is not on screen');
  }

  await window.mouse.move(outlet.x + outlet.width / 2, outlet.y + outlet.height / 2);
  await window.mouse.down();
  await window.mouse.move(target.x + target.width / 2, target.y + target.height / 2, { steps: 8 });
  await window.mouse.up();
  await expect(cable).toHaveCount(1);

  // A cable cannot skip a column: dragging from the drive to two columns over does nothing.
  await editor.addBlock(1, 6, 'Chorus');
  const far = await editor.cell(1, 6).boundingBox();
  await window.mouse.move(outlet.x + outlet.width / 2, outlet.y + outlet.height / 2);
  await window.mouse.down();
  await window.mouse.move(far!.x + far!.width / 2, far!.y + far!.height / 2, { steps: 8 });
  await window.mouse.up();
  await expect(window.getByTestId('cable-drive1>chorus1')).toHaveCount(0);

  // Select the cable and delete it.
  await cable.dispatchEvent('pointerdown');
  await editor.grid.press('Delete');
  await expect(cable).toHaveCount(0);
});

test('the picker stops offering a block type at its instance limit', async ({ window, editor }) => {
  // Two reverbs is the limit.
  await editor.addBlock(1, 2, 'Reverb');
  await editor.addBlock(1, 3, 'Reverb');
  await editor.cell(1, 4).dblclick();
  const reverb = window.getByRole('option', { name: 'Reverb, limit reached' });
  await expect(reverb).toHaveAttribute('aria-disabled', 'true');

  // Enter on a full type adds nothing.
  await window.getByRole('combobox', { name: 'Search blocks' }).fill('reverb');
  await window.keyboard.press('Enter');
  await window.keyboard.press('Escape');
  await expect(editor.cell(1, 4)).toHaveAccessibleName(/^Empty/);
});

test('a cable starts from the edge of a block and lands when dropped just short of the next one', async ({
  window,
  editor
}) => {
  await editor.addBlock(1, 2, 'Drive');
  await editor.addBlock(1, 3, 'Delay');
  const tile = await editor.cell(1, 2).boundingBox();
  const next = await editor.cell(1, 3).boundingBox();
  if (!tile || !next) {
    throw new Error('The blocks are not on screen');
  }

  // Grab inside the drive's right edge, not on the dot, and let go in the gap before the delay.
  const y = tile.y + tile.height / 2;
  await window.mouse.move(tile.x + tile.width - 6, y);
  await window.mouse.down();
  await window.mouse.move(next.x - 3, y, { steps: 6 });
  await expect(editor.cell(1, 3)).toHaveAttribute('data-wire', 'target');
  await window.mouse.up();

  await expect(window.getByTestId('cable-drive1>delay1')).toHaveCount(1);
  await expect(editor.block('Drive', 'drive1')).toHaveAccessibleName(/row 2, column 3$/);
  await expect(editor.cell(1, 3)).not.toHaveAttribute('data-wire');
});

test('removing a block from the signal path leaves a shunt, so the path stays connected', async ({
  window,
  editor
}) => {
  // The drive takes over a shunt on the main row, and its cables.
  await editor.addBlock(0, 2, 'Drive');
  await expect(window.getByTestId('cable-shunt-r0c1>drive1')).toHaveCount(1);
  await expect(window.getByTestId('cable-drive1>shunt-r0c3')).toHaveCount(1);

  await editor.block('Drive', 'drive1').click();
  await editor.grid.press('Delete');
  await expect(editor.cell(0, 2)).toHaveAccessibleName(/^Shunt/);
  await expect(window.getByTestId('cable-shunt-r0c1>shunt-r0c2')).toHaveCount(1);
  await expect(window.getByTestId('cable-shunt-r0c2>shunt-r0c3')).toHaveCount(1);
  // Only the output's shunt has no cable out, as with an untouched preset: nothing is left dangling.
  await expect(window.locator('[data-testid^="outlet-shunt"]')).toHaveCount(0);

  await window.getByRole('button', { name: 'Undo' }).click();
  await expect(editor.block('Drive', 'drive1')).toHaveAccessibleName(/row 1, column 3$/);
});
