import { expect, test } from './fixtures';

// The pages a player uses on stage: setlists, the Perform page, scenes, controllers and footswitches.

test('a setlist is made from the library and stepped through from the Perform page and the keyboard', async ({
  window
}) => {
  await window.getByRole('link', { name: 'Setlists' }).click();
  await window.getByTestId('setlist-new').click();
  await window.getByTestId('setlist-rename-field').fill('Friday');
  await window.getByRole('dialog', { name: 'Rename setlist' }).getByRole('button', { name: 'Rename' }).click();
  await expect(window.getByTestId('setlist-name')).toHaveText('Friday');

  // Three songs from the factory presets: one with the + button, one by double-click, one with Enter. Each is
  // stored before the next is added.
  await window.getByRole('searchbox', { name: 'Filter presets' }).fill('a');
  const library = window.getByTestId('setlist-library');
  const songs = window.getByTestId('setlist-songs').getByRole('listitem');
  await library.getByRole('button', { name: 'Add Apache' }).click();
  await expect(songs).toHaveCount(1);
  await library.getByText('Back in Black', { exact: true }).dblclick();
  await expect(songs).toHaveCount(2);
  await library.getByRole('listitem', { name: /Basket Case/ }).press('Enter');
  await expect(songs).toHaveCount(3);

  // The second song gets a name of its own and starts on scene 2; then it moves to the top.
  await window.getByRole('textbox', { name: 'Name of song 2' }).fill('Opener');
  await window.getByRole('textbox', { name: 'Name of song 2' }).press('Enter');
  await window.getByLabel('Starting scene of song 2').getByRole('combobox').click();
  await window.getByRole('option', { name: 'Scene 2', exact: true }).click();
  await window.getByRole('button', { name: 'Move song 2 earlier' }).click();
  await expect(songs.first()).toHaveAccessibleName('Song 1: Opener');
  await expect(songs.nth(1)).toHaveAccessibleName('Song 2: Apache');

  // Play it: the Perform page shows the set, not started.
  await window.getByTestId('setlist-play').click();
  await expect(window).toHaveURL(/#\/perform/);
  const current = window.getByTestId('song-current');
  await expect(current).toContainText('Not started');
  await expect(window.getByTestId('song-next')).toContainText('Opener');

  // Next song loads the first song's preset on its scene.
  await window.getByTestId('song-next-button').click();
  await expect(current).toContainText('Opener');
  await expect(window.getByTestId('preset-name')).toHaveText('Back in Black');
  await expect(window.getByRole('radiogroup', { name: 'Scene' }).getByRole('radio', { name: '2' })).toBeChecked();

  // ⇧⌘PgDn and ⇧⌘PgUp step through the set from anywhere.
  await window.getByRole('link', { name: 'Editor' }).click();
  await window.keyboard.press('ControlOrMeta+Shift+PageDown');
  await expect(window.getByTestId('preset-name')).toHaveText('Apache');
  await window.keyboard.press('ControlOrMeta+Shift+PageDown');
  await expect(window.getByTestId('preset-name')).toHaveText('Basket Case');
  await window.keyboard.press('ControlOrMeta+Shift+PageUp');
  await expect(window.getByTestId('preset-name')).toHaveText('Apache');

  // The set is stored: the Setlists page still has it, marked as playing.
  await window.getByRole('link', { name: 'Setlists' }).click();
  await expect(window.getByRole('listbox', { name: 'Setlists' }).getByRole('option', { name: /Friday/ })).toBeVisible();
  await expect(window.getByTestId('playing-tag')).toBeVisible();
});

test('a knob goes onto the Perform page, where it is renamed, turned and removed', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Delay');
  await editor.knob('Mix').click({ button: 'right' });
  await window.getByRole('menuitem', { name: "Add to the preset's Perform page" }).click();
  await editor.knob('Feedback').click({ button: 'right' });
  await window.getByRole('menuitem', { name: "Add to the preset's Perform page" }).click();

  await window.getByRole('link', { name: 'Perform' }).click();
  const grid = window.getByTestId('perform-grid');
  const controls = grid.getByRole('toolbar');
  await expect(controls).toHaveCount(2);
  await expect(controls.first()).toHaveAccessibleName(/Mix/);

  // Turning the control here turns the block's knob.
  const mix = grid.getByRole('slider', { name: /Mix/ });
  await mix.focus();
  const before = await mix.getAttribute('aria-valuenow');
  await mix.press('PageUp');
  await expect(mix).not.toHaveAttribute('aria-valuenow', before ?? '');
  const turned = await mix.getAttribute('aria-valuenow');

  // Move it after the other one, and give it a name for the stage.
  await window.getByRole('button', { name: /^Move .*Mix later$/ }).click();
  await expect(controls.first()).toHaveAccessibleName(/Feedback/);
  await window.getByRole('button', { name: /^Rename .*Mix$/ }).click();
  await window.getByTestId('perform-label').fill('Echo level');
  await window.getByTestId('perform-label').press('Enter');
  await expect(controls.nth(1)).toHaveAccessibleName('Echo level');

  // The block's own page shows the value set from here.
  await window.getByRole('link', { name: 'Editor' }).click();
  await editor.block('Delay', 'delay1').click();
  await expect(editor.knob('Mix')).toHaveAttribute('aria-valuenow', turned ?? '');

  // Remove the other control: one is left.
  await window.getByRole('link', { name: 'Perform' }).click();
  await window.getByRole('button', { name: /^Remove .*Feedback$/ }).click();
  await expect(controls).toHaveCount(1);
});

test('scenes are named, edited in the matrix, swapped and copied', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Drive');

  // Name scene 1 from the toolbar.
  await window.getByTestId('scene-name').click();
  await window.getByTestId('scene-name-input').fill('Rhythm');
  await window.getByTestId('scene-name-input').press('Enter');
  await expect(window.getByTestId('scene-name')).toContainText('Rhythm');

  await window.getByRole('link', { name: 'Scenes' }).click();
  const matrix = window.getByTestId('scene-matrix');
  await expect(matrix.getByRole('columnheader').nth(1)).toContainText('Rhythm');

  // Scene 2 runs the Drive bypassed, on channel B.
  await matrix.getByRole('button', { name: 'Drive (drive1) in scene 2: engaged' }).click();
  await expect(matrix.getByRole('button', { name: 'Drive (drive1) in scene 2: bypassed' })).toBeVisible();
  await matrix.getByRole('button', { name: 'Drive (drive1) in scene 2: channel B' }).click();
  await expect(matrix.getByRole('button', { name: 'Drive (drive1) in scene 2: channel B' })).toHaveAttribute(
    'aria-pressed',
    'true'
  );

  // Swap scenes 1 and 2: the name and the block states change places.
  await window.getByTestId('swap-scenes').click();
  await expect(matrix.getByRole('columnheader').nth(2)).toContainText('Rhythm');
  await expect(matrix.getByRole('button', { name: 'Drive (drive1) in scene 1: bypassed' })).toBeVisible();
  await expect(matrix.getByRole('button', { name: 'Drive (drive1) in scene 2: engaged' })).toBeVisible();

  // Copy scene 1 onto scene 2: both bypass the Drive on channel B.
  await window.getByTestId('copy-scene').click();
  await expect(matrix.getByRole('button', { name: 'Drive (drive1) in scene 2: bypassed' })).toBeVisible();
  await expect(matrix.getByRole('button', { name: 'Drive (drive1) in scene 2: channel B' })).toHaveAttribute(
    'aria-pressed',
    'true'
  );

  // The grid shows what the matrix set: the Drive is bypassed on the scene that plays.
  await window.getByRole('link', { name: 'Editor' }).click();
  await expect(editor.block('Drive', 'drive1')).toHaveAccessibleName(/bypassed/);
});

test('the Controllers page holds scene controllers, scene MIDI and the preset’s modifiers', async ({
  window,
  editor
}) => {
  // A modifier to list: an LFO on the Delay's Mix.
  await editor.addBlock(2, 2, 'Delay');
  await editor.knob('Mix').click({ button: 'right' });
  await window.getByRole('menuitem', { name: 'Add modifier…' }).click();
  await window.getByTestId('modifier-save').click();
  await expect(window.getByLabel('Modified by lfo1')).toBeVisible();

  await window.getByRole('link', { name: 'Controllers' }).click();
  await expect(window.getByTestId('modifier-list')).toContainText('delay1');
  await expect(window.getByTestId('modifier-list')).toContainText('lfo1');

  // A scene controller is set per scene: scene 2 keeps its own value.
  const controller = window.getByRole('slider', { name: 'Controller 1' });
  await controller.focus();
  await controller.press('End');
  await expect(controller).toHaveAttribute('aria-valuenow', '100');
  await editor.segment('Scene', '2').click();
  await expect(window.getByRole('heading', { name: 'Scene 2', exact: true })).toBeVisible();
  await expect(controller).toHaveAttribute('aria-valuenow', '0');
  await editor.segment('Scene', '1').click();
  await expect(controller).toHaveAttribute('aria-valuenow', '100');

  // A MIDI message the scene sends is added and removed.
  const midi = window.getByRole('region', { name: 'Scene 1 MIDI' });
  await midi.getByRole('button', { name: 'Add message' }).click();
  await expect(midi.getByLabel('Message 1 type')).toBeVisible();
  await midi.getByRole('button', { name: 'Remove message 1' }).click();
  await expect(midi.getByText('This scene sends no MIDI.')).toBeVisible();

  // The LFO the modifier follows runs: its rate is set here.
  const rate = window.getByRole('region', { name: 'LFO 1' }).getByRole('slider', { name: 'Rate' });
  const before = await rate.getAttribute('aria-valuenow');
  await rate.focus();
  await rate.press('PageUp');
  await expect(rate).not.toHaveAttribute('aria-valuenow', before ?? '');
});

test('MIDI learn is armed and cancelled, and a footswitch layout gets switches and views', async ({ window }) => {
  await window.getByRole('link', { name: 'Global' }).click();

  // Arm MIDI learn for a scene change, then cancel it: nothing is mapped.
  const midi = window.getByRole('region', { name: 'MIDI' });
  await midi.getByRole('button', { name: 'MIDI learn' }).click();
  await expect(window.getByTestId('midi-learn-armed')).toContainText('Learning');
  await expect(midi.getByRole('button', { name: 'Move a control…' })).toBeVisible();
  await midi.getByRole('button', { name: 'Cancel learn' }).click();
  await expect(window.getByTestId('midi-learn-armed')).toHaveCount(0);
  await expect(midi.getByRole('button', { name: 'MIDI learn' })).toBeVisible();
  await expect(midi.getByText('No mappings yet')).toBeVisible();

  // A layout with two switches on its first view.
  const footswitches = window.getByRole('region', { name: 'Footswitches' });
  await footswitches.getByRole('button', { name: 'New layout' }).click();
  await footswitches.getByRole('button', { name: 'Add switch' }).click();
  await footswitches.getByRole('button', { name: 'Add switch' }).click();
  const switches = footswitches.getByRole('table', { name: 'Footswitches on this view' });
  await expect(switches.getByLabel('Switch 2 tap action')).toBeVisible();
  await switches.getByLabel('Switch 1 label').fill('Verse');
  await switches.getByLabel('Switch 1 label').press('Enter');

  // A second view, named, with a switch of its own; the first view keeps its two.
  const views = window.getByTestId('footswitch-views');
  await views.getByRole('button', { name: 'View', exact: true }).click();
  await views.getByRole('button', { name: 'View 2' }).click();
  await views.getByRole('button', { name: 'Rename view View 2' }).click();
  await window.getByTestId('view-name').fill('Solos');
  await window.getByTestId('view-name').press('Enter');
  await expect(views.getByRole('button', { name: 'Solos', exact: true })).toBeVisible();
  await expect(switches.getByLabel('Switch 1 tap action')).toHaveCount(0);
  await footswitches.getByRole('button', { name: 'Add switch' }).click();
  await expect(switches.getByLabel(/^Switch \d+ tap action$/)).toHaveCount(1);

  // Make it the active view, then go back to the first: its switches and the label are still there.
  await views.getByRole('button', { name: 'Make active' }).click();
  await expect(views.getByRole('button', { name: 'Solos (active)' })).toBeVisible();
  await views.getByRole('button', { name: 'Main' }).click();
  await expect(switches.getByLabel(/^Switch \d+ tap action$/)).toHaveCount(2);
  await expect(switches.getByLabel('Switch 1 label')).toHaveValue('Verse');

  // Remove the second view: one view is left, and it is the active one again.
  await views.getByRole('button', { name: 'Solos (active)' }).click();
  await views.getByRole('button', { name: 'Remove view Solos' }).click();
  await expect(views.getByRole('button', { name: /Solos/ })).toHaveCount(0);
  await expect(views.getByRole('button', { name: 'Main (active)' })).toBeVisible();

  // The layout is stored: a reload shows it.
  await window.reload();
  await expect(window.getByRole('region', { name: 'Footswitches' }).getByLabel('Switch 1 label')).toHaveValue('Verse');
});

test('a knob on the global Perform page shows in every preset that has its block', async ({ window, editor }) => {
  await editor.addBlock(2, 2, 'Delay');
  await editor.knob('Mix').click({ button: 'right' });
  await window.getByRole('menuitem', { name: 'Add to the global Perform page' }).click();

  // It is on the Global page, not the preset's.
  await window.getByRole('link', { name: 'Perform' }).click();
  const pages = window.getByRole('radiogroup', { name: 'Page' });
  await expect(window.getByText('Nothing on this page yet')).toBeVisible();
  await pages.getByRole('radio', { name: 'Global' }).click();
  const grid = window.getByTestId('perform-grid');
  await expect(grid.getByRole('toolbar')).toHaveCount(1);

  // Turning it turns the block's knob.
  const mix = grid.getByRole('slider', { name: /Mix/ });
  await mix.focus();
  await mix.press('End');
  await expect(mix).toHaveAttribute('aria-valuenow', '100');
  await window.getByRole('link', { name: 'Editor' }).click();
  await editor.block('Delay', 'delay1').click();
  await expect(editor.knob('Mix')).toHaveAttribute('aria-valuenow', '100');

  // A preset without that block has nothing to show for it; with the block back, the control is back.
  window.once('dialog', dialog => void dialog.accept().catch(() => undefined));
  await window.getByRole('button', { name: 'New preset', exact: true }).click();
  await expect(editor.block('Delay', 'delay1')).toHaveCount(0);
  await window.getByRole('link', { name: 'Perform' }).click();
  await pages.getByRole('radio', { name: 'Global' }).click();
  await expect(grid.getByRole('slider', { name: /Mix/ })).toHaveCount(0);

  await window.getByRole('link', { name: 'Editor' }).click();
  await editor.addBlock(2, 2, 'Delay');
  await window.getByRole('link', { name: 'Perform' }).click();
  await pages.getByRole('radio', { name: 'Global' }).click();
  await expect(grid.getByRole('slider', { name: /Mix/ })).toBeVisible();

  // Removed here, it is gone for every preset.
  await window.getByRole('button', { name: /^Remove .*Mix$/ }).click();
  await expect(window.getByText('Nothing on this page yet')).toBeVisible();
});

test('a footswitch goes on to another layout, and the preset gives it an action and a label of its own', async ({
  window
}) => {
  await window.getByRole('link', { name: 'Global' }).click();
  const footswitches = window.getByRole('region', { name: 'Footswitches' });
  const active = footswitches.getByRole('combobox', { name: 'Active footswitch layout' });

  // Two layouts; the second, just made, is the active one and gets a switch.
  await footswitches.getByRole('button', { name: 'New layout' }).click();
  await expect(active).toContainText('Layout 1');
  await footswitches.getByRole('button', { name: 'New layout' }).click();
  await expect(active).toContainText('Layout 2');
  await footswitches.getByRole('button', { name: 'Add switch' }).click();
  const switches = footswitches.getByRole('table', { name: 'Footswitches on this view' });
  await expect(switches.getByLabel('Switch 1 label')).toBeVisible();

  // After its action the switch goes on to Layout 1.
  const link = switches.getByLabel('Switch 1 layout link').getByRole('combobox');
  await link.click();
  await window.getByRole('option', { name: 'Layout 1', exact: true }).click();
  await expect(link).toContainText('Layout 1');

  // The loaded preset gives the switch its own action: that is an edit to the preset, and it brings a hold action and
  // a label of its own.
  await expect(window.getByLabel('unsaved changes')).toHaveCount(0);
  const own = switches.getByRole('combobox', { name: 'Switch 1 in this preset action' });
  await own.click();
  await window.getByRole('option', { name: 'tuner', exact: true }).click();
  await expect(own).toContainText('tuner');
  await expect(window.getByLabel('unsaved changes')).toBeVisible();
  await expect(switches.getByRole('combobox', { name: 'Switch 1 hold in this preset action' })).toBeVisible();
  const label = switches.getByLabel('In this preset, label of switch 1');
  await label.fill('Tune');
  await label.press('Enter');

  // The link is a global setting and the override is in the working copy: both are there after a reload.
  await window.reload();
  const reloaded = window.getByRole('region', { name: 'Footswitches' });
  const linkAgain = reloaded.getByLabel('Switch 1 layout link').getByRole('combobox');
  await expect(linkAgain).toContainText('Layout 1');
  await expect(reloaded.getByRole('combobox', { name: 'Switch 1 in this preset action' })).toContainText('tuner');
  await expect(reloaded.getByLabel('In this preset, label of switch 1')).toHaveValue('Tune');

  // (none) takes the preset's own action away, and its hold action and label with it.
  await reloaded.getByRole('combobox', { name: 'Switch 1 in this preset action' }).click();
  await window.getByRole('option', { name: '(none)', exact: true }).click();
  await expect(reloaded.getByLabel('In this preset, label of switch 1')).toHaveCount(0);
  await expect(reloaded.getByRole('combobox', { name: 'Switch 1 hold in this preset action' })).toHaveCount(0);

  // Deleting the layout the switch goes on to leaves the switch where it is.
  await reloaded.getByRole('combobox', { name: 'Active footswitch layout' }).click();
  await window.getByRole('option', { name: 'Layout 1', exact: true }).click();
  await window.getByTestId('delete-layout').click();
  await window.getByRole('alertdialog').getByRole('button', { name: 'Delete' }).click();
  await expect(reloaded.getByRole('combobox', { name: 'Active footswitch layout' })).toContainText('Layout 2');
  await expect(linkAgain).not.toContainText('Layout 1');
});
