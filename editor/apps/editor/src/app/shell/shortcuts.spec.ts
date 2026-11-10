import {
  ShortcutGroups,
  ShortcutService,
  Shortcuts,
  controlTakesKey,
  findShortcut,
  hasSelectedText,
  hintedShortcut,
  isDialogOpen,
  isOnControl,
  isTyping,
  shortcutHint,
  shortcutKeys,
  shortcutLabel,
  shortcutRows
} from './shortcuts';

function key(key: string, options: Partial<KeyboardEventInit> & { code?: string } = {}): KeyboardEvent {
  return new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...options });
}

describe('keyboard shortcuts', () => {
  it('maps each binding to one command, with no two sharing keys', () => {
    const bindings = Shortcuts.map(
      s => `${s.mod ? 'mod+' : ''}${s.shift ? 'shift+' : ''}${s.code ?? s.key?.toLowerCase()}`
    );
    expect(new Set(bindings).size).toBe(bindings.length);
    expect(new Set(Shortcuts.map(s => s.id)).size).toBe(Shortcuts.length);
  });

  it('leaves the desktop menu’s accelerators alone (Electron’s default menu)', () => {
    const menu = ['mod+a', 'mod+r', 'mod+w', 'mod+m', 'mod+q', 'mod+h', 'mod+shift+r', 'mod+0', 'mod+='];
    const ours = Shortcuts.map(
      s => `${s.mod ? 'mod+' : ''}${s.shift ? 'shift+' : ''}${s.code ?? s.key?.toLowerCase()}`
    );
    expect(ours.filter(b => menu.includes(b))).toEqual([]);
  });

  it('finds the command a key press means, modifiers exactly', () => {
    expect(findShortcut(key('p'))?.id).toBe('presetPicker');
    expect(findShortcut(key('P', { metaKey: true }))).toBeNull();
    expect(findShortcut(key('PageDown', { metaKey: true }))?.id).toBe('presetNext');
    expect(findShortcut(key('g', { ctrlKey: true }))).toBeNull();
    expect(findShortcut(key('g'))?.id).toBe('global');
    expect(findShortcut(key('F5'))?.id).toBe('reload');
    expect(findShortcut(key('3', { metaKey: true, code: 'Digit3' }))?.id).toBe('scene3');
    expect(findShortcut(key('c'))?.id).toBe('channelC');
    expect(findShortcut(key('x'))?.id).toBe('channelNext');
    expect(findShortcut(key('x', { metaKey: true }))?.id).toBe('cut');
    expect(findShortcut(key(' ', { code: 'Space' }))?.id).toBe('bypass');
    expect(findShortcut(key(' ', { code: 'Space', shiftKey: true }))?.id).toBe('engageAll');
    expect(findShortcut(key(' ', { code: 'Space', shiftKey: true, metaKey: true }))?.id).toBe('bypassAll');
    expect(findShortcut(key('b'))?.id).toBe('channelB');
    expect(findShortcut(key('B', { shiftKey: true }))).toBeNull();
    expect(findShortcut(key('b', { metaKey: true }))?.id).toBe('saveBlock');
    expect(findShortcut(key('d', { metaKey: true }))?.id).toBe('disconnect');
    expect(findShortcut(key('u', { metaKey: true }))?.id).toBe('shunt');
    expect(findShortcut(key('q'))?.id).toBe('addBlock');
    expect(findShortcut(key('F1'))?.id).toBe('blockInfo');
    expect(findShortcut(key('t', { metaKey: true }))?.id).toBe('tuner');
    expect(findShortcut(key('t'))?.id).toBe('tunerAlias');
    expect(findShortcut(key('y', { metaKey: true }))?.id).toBe('redoAlias');
    expect(findShortcut(key('z', { metaKey: true, shiftKey: true }))?.id).toBe('redo');
    expect(findShortcut(key('?', { shiftKey: true }))?.id).toBe('help');
    expect(findShortcut(key(',', { metaKey: true }))?.id).toBe('settings');
  });

  it('reads as the platform writes it', () => {
    const saveAs = Shortcuts.find(s => s.id === 'saveAs');
    const scene8 = Shortcuts.find(s => s.id === 'scene8');
    if (!saveAs || !scene8) {
      throw new Error('No saveAs or scene8 shortcut');
    }

    expect(shortcutKeys(saveAs, true)).toEqual(['⌘', '⇧', 'S']);
    expect(shortcutKeys(saveAs, false)).toEqual(['Ctrl', 'Shift', 'S']);
    expect(shortcutKeys(scene8, true)).toEqual(['⌘', '8']);
  });

  it('gives the tuner a bare key as well, because a browser keeps ⌘T and Ctrl+T for a new tab', () => {
    const service = new ShortcutService();
    let toggled = 0;
    service.register({ tuner: () => toggled++ });

    const bare = key('t');
    document.body.dispatchEvent(bare);
    expect(service.dispatch(bare)).toBe(true);
    expect(bare.defaultPrevented).toBe(true);
    expect(service.dispatch(key('t', { ctrlKey: true }))).toBe(true);
    expect(toggled).toBe(2);

    // It is a letter like any other: typed into a field it is text.
    const field = document.createElement('input');
    document.body.append(field);
    const typed = key('t');
    field.dispatchEvent(typed);
    expect(service.dispatch(typed)).toBe(false);
    field.remove();

    expect(shortcutHint('tunerAlias', true)).toBe('T');
    expect(shortcutHint('tunerAlias', false)).toBe('T');
  });

  // #260: a browser may keep the keys that switch its tabs and reach its address bar, so each command on one of
  // them has a bare key as well.
  it('gives preset and song stepping, the scenes and Preset Leveling bare keys a browser delivers', () => {
    expect(findShortcut(key('PageUp'))?.id).toBe('presetPreviousAlias');
    expect(findShortcut(key('PageDown'))?.id).toBe('presetNextAlias');
    expect(findShortcut(key('PageUp', { shiftKey: true }))?.id).toBe('songPreviousAlias');
    expect(findShortcut(key('PageDown', { shiftKey: true }))?.id).toBe('songNextAlias');
    expect(findShortcut(key('l'))?.id).toBe('levelAlias');
    expect(findShortcut(key('5', { code: 'Digit5' }))?.id).toBe('scene5Alias');
    // By the key's place, as ⌘5 is: on a French keyboard the unshifted key types "(".
    expect(findShortcut(key('(', { code: 'Digit5' }))?.id).toBe('scene5Alias');
    expect(findShortcut(key('9', { code: 'Digit9' }))).toBeNull();

    const browserKept = ['tuner', 'level', 'presetPrevious', 'presetNext', 'songPrevious', 'songNext'].concat(
      [1, 2, 3, 4, 5, 6, 7, 8].map(n => `scene${n}`)
    );
    for (const id of browserKept) {
      const alias = Shortcuts.find(s => s.aliasOf === id && s.browser);
      expect(alias, id).toBeDefined();
      // A browser keeps keys with ⌘, Ctrl or Alt for itself, never a bare one.
      expect(alias?.mod ?? alias?.alt ?? false, id).toBe(false);
    }
  });

  it('runs the command’s handler for an alias, under the command’s rules', () => {
    const service = new ShortcutService();
    const ran: string[] = [];
    service.register({
      presetNext: () => ran.push('presetNext'),
      songPrevious: () => ran.push('songPrevious'),
      level: () => ran.push('level'),
      scene3: () => ran.push('scene3'),
      redo: () => ran.push('redo'),
      remove: () => ran.push('remove')
    });

    const pageDown = key('PageDown');
    document.body.dispatchEvent(pageDown);
    expect(service.dispatch(pageDown)).toBe(true);
    expect(pageDown.defaultPrevented).toBe(true); // the page does not scroll as well
    for (const event of [
      key('PageUp', { shiftKey: true }),
      key('l'),
      key('3', { code: 'Digit3' }),
      key('y', { ctrlKey: true }),
      key('Backspace')
    ]) {
      document.body.dispatchEvent(event);
      service.dispatch(event);
    }

    expect(ran).toEqual(['presetNext', 'songPrevious', 'level', 'scene3', 'redo', 'remove']);

    // A focused knob keeps its Page keys (a tenth of its travel); the digit still changes scene from it.
    const knob = document.createElement('div');
    knob.setAttribute('role', 'slider');
    document.body.append(knob);
    const onKnob = key('PageDown');
    knob.dispatchEvent(onKnob);
    expect(service.dispatch(onKnob)).toBe(false);
    const digit = key('3', { code: 'Digit3' });
    knob.dispatchEvent(digit);
    expect(service.dispatch(digit)).toBe(true);
    knob.remove();

    // Behind a dialog an alias waits as its command does.
    const dialog = document.createElement('div');
    dialog.setAttribute('aria-modal', 'true');
    document.body.append(dialog);
    const behind = key('PageDown');
    document.body.dispatchEvent(behind);
    expect(service.dispatch(behind)).toBe(false);
    expect(behind.defaultPrevented).toBe(false); // so Page Down still scrolls the dialog
    dialog.remove();
  });

  it('names the key a browser delivers in a browser, and the command’s own in the desktop app', () => {
    expect(hintedShortcut('tuner', true)).toBe('tuner');
    expect(hintedShortcut('tuner', false)).toBe('tunerAlias');
    expect(shortcutHint(hintedShortcut('presetNext', false), true)).toBe('PgDn');
    expect(shortcutHint(hintedShortcut('songNext', false), true)).toBe('⇧PgDn');
    expect(shortcutHint(hintedShortcut('songNext', false), false)).toBe('Shift+PgDn');
    expect(shortcutHint(hintedShortcut('scene2', false), false)).toBe('2');
    expect(shortcutHint(hintedShortcut('level', false), true)).toBe('L');
    // A key no browser keeps is named as it is, and so is a command with an alias that is only a second key.
    expect(hintedShortcut('save', false)).toBe('save');
    expect(hintedShortcut('redo', false)).toBe('redo');
  });

  it('lists a command once, with the keys of each of its bindings', () => {
    const rows = ShortcutGroups.flatMap(group => shortcutRows(group, true));
    const row = (id: string) => rows.find(r => r.id === id);

    expect(row('tuner')?.bindings).toEqual([['⌘', 'T'], ['T']]);
    expect(row('redo')?.bindings).toEqual([
      ['⌘', '⇧', 'Z'],
      ['⌘', 'Y']
    ]);
    expect(row('remove')?.bindings).toEqual([['Del'], ['⌫']]);
    expect(row('songNext')?.bindings).toEqual([
      ['⌘', '⇧', 'PgDn'],
      ['⇧', 'PgDn']
    ]);
    expect(row('scene8')?.bindings).toEqual([['⌘', '8'], ['8']]);
    expect(row('save')?.bindings).toEqual([['⌘', 'S']]);

    // Every shortcut is on exactly one row: its own, or its command's.
    expect(rows.map(r => r.id)).toEqual(Shortcuts.filter(s => !s.aliasOf).map(s => s.id));
    for (const alias of Shortcuts.filter(s => s.aliasOf)) {
      expect(row(alias.aliasOf ?? ''), alias.id).toBeDefined();
      expect(Shortcuts.find(s => s.id === alias.aliasOf)?.group, alias.id).toBe(alias.group);
    }
  });

  it('writes a hint for a tooltip or a menu item with the keys the keyboard has', () => {
    expect(shortcutHint('save', true)).toBe('⌘S');
    expect(shortcutHint('saveAs', true)).toBe('⇧⌘S');
    expect(shortcutHint('bypassAll', true)).toBe('⇧⌘Space');
    expect(shortcutHint('songNext', true)).toBe('⇧⌘PgDn');
    expect(shortcutHint('settings', true)).toBe('⌘,');
    expect(shortcutHint('help', true)).toBe('?');

    expect(shortcutHint('save', false)).toBe('Ctrl+S');
    expect(shortcutHint('saveAs', false)).toBe('Ctrl+Shift+S');
    expect(shortcutHint('bypassAll', false)).toBe('Ctrl+Shift+Space');
    expect(shortcutHint('engageAll', false)).toBe('Shift+Space');
    expect(shortcutHint('songNext', false)).toBe('Ctrl+Shift+PgDn');
    expect(shortcutHint('mute', false)).toBe('M');
    expect(() => shortcutHint('nothing')).toThrow();
  });

  it('shows no Mac key symbol off macOS, in any shortcut’s keys, hint or label', () => {
    for (const shortcut of Shortcuts) {
      const shown = [
        ...shortcutKeys(shortcut, false),
        shortcutHint(shortcut.id, false),
        shortcutLabel(shortcut, false)
      ];
      expect(shown.join(' ')).not.toMatch(/[⌘⌥⇧⌃]/);
    }

    const blockInfo = Shortcuts.find(s => s.id === 'blockInfo');
    if (!blockInfo) {
      throw new Error('No blockInfo shortcut');
    }

    expect(shortcutLabel(blockInfo, true)).toBe('Block Info (or hold ⌥)');
    expect(shortcutLabel(blockInfo, false)).toBe('Block Info (or hold Alt)');
  });

  it('stays out of text fields but not checkboxes', () => {
    const text = document.createElement('input');
    const box = Object.assign(document.createElement('input'), { type: 'checkbox' });
    const area = document.createElement('textarea');
    expect(isTyping(text)).toBe(true);
    expect(isTyping(area)).toBe(true);
    expect(isTyping(box)).toBe(false);
    expect(isTyping(document.body)).toBe(false);
  });

  it('dispatches to the registered handler, consumes the key, and stops when unregistered', () => {
    const service = new ShortcutService();
    let ran = 0;
    const unregister = service.register({ tuner: () => ran++ });

    const press = key('t', { metaKey: true });
    document.body.dispatchEvent(press);
    expect(service.dispatch(press)).toBe(true);
    expect(press.defaultPrevented).toBe(true);
    unregister();
    expect(service.dispatch(key('t', { metaKey: true }))).toBe(false);
    expect(ran).toBe(1);
  });

  it('ignores keys typed into a text field', () => {
    const service = new ShortcutService();
    let ran = 0;
    service.register({ presetPicker: () => ran++ });
    const input = document.createElement('input');
    document.body.append(input);

    const press = key('p');
    input.dispatchEvent(press);
    expect(service.dispatch(press)).toBe(false);
    expect(ran).toBe(0);
    input.remove();
  });

  it('leaves Space and Delete to a focused button, switch, checkbox or slider', () => {
    const service = new ShortcutService();
    const ran: string[] = [];
    service.register({
      bypass: () => ran.push('bypass'),
      remove: () => ran.push('remove'),
      channelB: () => ran.push('channelB'),
      tuner: () => ran.push('tuner'),
      blockInfo: () => ran.push('blockInfo')
    });
    const button = document.createElement('button');
    button.append(document.createElement('span'));
    const toggle = Object.assign(document.createElement('div'), { tabIndex: 0 });
    toggle.setAttribute('role', 'switch');
    const box = Object.assign(document.createElement('input'), { type: 'checkbox' });
    const knob = document.createElement('div');
    knob.setAttribute('role', 'slider');
    document.body.append(button, toggle, box, knob);

    const icon = button.firstElementChild;
    if (!icon) {
      throw new Error('The button has no child');
    }

    for (const target of [button, icon, toggle, box, knob]) {
      for (const press of [key(' ', { code: 'Space' }), key('Delete')]) {
        target.dispatchEvent(press);
        expect(service.dispatch(press)).toBe(false);
        expect(press.defaultPrevented).toBe(false);
      }
    }

    // ⌘ shortcuts and the function keys still work from a control.
    const tuner = key('t', { metaKey: true });
    button.dispatchEvent(tuner);
    expect(service.dispatch(tuner)).toBe(true);
    const info = key('F1');
    knob.dispatchEvent(info);
    expect(service.dispatch(info)).toBe(true);
    expect(ran).toEqual(['tuner', 'blockInfo']);
    button.remove();
    toggle.remove();
    box.remove();
    knob.remove();
  });

  it('runs letters from a button, switch, checkbox or slider, but leaves them to a list or menu', () => {
    const service = new ShortcutService();
    let muted = 0;
    service.register({ mute: () => muted++ });
    const metronome = document.createElement('button');
    const toggle = document.createElement('div');
    toggle.setAttribute('role', 'switch');
    const knob = document.createElement('div');
    knob.setAttribute('role', 'slider');
    const list = document.createElement('ul');
    list.setAttribute('role', 'listbox');
    const option = document.createElement('li');
    option.setAttribute('role', 'option');
    list.append(option);
    const menu = document.createElement('div');
    menu.setAttribute('role', 'menu');
    document.body.append(metronome, toggle, knob, list, menu);

    for (const target of [metronome, toggle, knob]) {
      const press = key('m');
      target.dispatchEvent(press);
      expect(service.dispatch(press)).toBe(true);
    }

    for (const target of [option, list, menu]) {
      const press = key('m');
      target.dispatchEvent(press);
      expect(service.dispatch(press)).toBe(false);
    }

    expect(muted).toBe(3);
    expect(controlTakesKey(metronome, ' ')).toBe(true);
    expect(controlTakesKey(metronome, 'Delete')).toBe(true);
    expect(controlTakesKey(document.body, ' ')).toBe(false);
    for (const element of [metronome, toggle, knob, list, menu]) {
      element.remove();
    }
  });

  it('bypasses with Space from the grid and the page itself', () => {
    const service = new ShortcutService();
    let ran = 0;
    service.register({ bypass: () => ran++ });
    const grid = Object.assign(document.createElement('div'), { tabIndex: 0 });
    grid.setAttribute('role', 'grid');
    document.body.append(grid);

    for (const target of [grid, document.body]) {
      const press = key(' ', { code: 'Space' });
      target.dispatchEvent(press);
      expect(service.dispatch(press)).toBe(true);
    }

    expect(ran).toBe(2);
    expect(isOnControl(grid)).toBe(false);
    grid.remove();
  });

  it('shows Block Info while ⌥ is held', () => {
    const service = new ShortcutService();
    service.dispatch(key('Alt', { altKey: true }));
    expect(service.altHeld()).toBe(true);
    service.keyUp(new KeyboardEvent('keyup', { key: 'Alt' }));
    expect(service.altHeld()).toBe(false);
  });
});

describe('ShortcutService.dispatch and the focused element', () => {
  afterEach(() => document.body.replaceChildren());

  /** Presses a key on an element, as the shell does: the event reaches the element, then the service sees it. */
  function press(service: ShortcutService, target: Element, event: KeyboardEvent): boolean {
    target.dispatchEvent(event);
    return service.dispatch(event);
  }

  function element<K extends keyof HTMLElementTagNameMap>(
    tag: K,
    attributes: Record<string, string> = {}
  ): HTMLElementTagNameMap[K] {
    const created = document.createElement(tag);
    for (const [name, value] of Object.entries(attributes)) {
      created.setAttribute(name, value);
    }

    document.body.append(created);
    return created;
  }

  function service() {
    const ran: string[] = [];
    const shortcuts = new ShortcutService();
    const ids = ['presetPicker', 'bypass', 'save', 'undo', 'tuner', 'reload', 'help'];
    shortcuts.register(Object.fromEntries(ids.map(id => [id, () => ran.push(id)])));
    return { shortcuts, ran };
  }

  it('runs nothing, not even ⌘ shortcuts, while the user types in a field, so the field keeps its own keys', () => {
    const { shortcuts, ran } = service();
    const editable = element('div', { contenteditable: 'true' });
    const inEditable = editable.appendChild(document.createElement('span'));
    const fields = [
      element('input'),
      element('input', { type: 'number' }),
      element('input', { type: 'search' }),
      element('textarea'),
      element('select'),
      element('div', { contenteditable: '' }),
      editable,
      inEditable
    ];

    for (const field of fields) {
      const presses = [key('p'), key(' ', { code: 'Space' }), key('s', { metaKey: true }), key('z', { ctrlKey: true })];
      for (const event of presses) {
        expect(press(shortcuts, field, event)).toBe(false);
        expect(event.defaultPrevented).toBe(false);
      }
    }

    expect(ran).toEqual([]);
  });

  it('leaves ⌘A to a text field, and keeps it from selecting the whole editor elsewhere', () => {
    const { shortcuts } = service();
    const inField = key('a', { metaKey: true });
    press(shortcuts, element('input'), inField);
    expect(inField.defaultPrevented).toBe(false);

    const onPage = key('a', { metaKey: true });
    expect(press(shortcuts, document.body, onPage)).toBe(false);
    expect(onPage.defaultPrevented).toBe(true);
  });

  it('treats radio, range, button and submit inputs as controls: Space stays theirs, letters, ⌘ and F keys work', () => {
    const { shortcuts, ran } = service();
    const types = ['radio', 'range', 'submit', 'button'];
    for (const type of types) {
      const input = element('input', { type });
      expect(press(shortcuts, input, key(' ', { code: 'Space' }))).toBe(false);
      expect(press(shortcuts, input, key('p'))).toBe(true);
      expect(press(shortcuts, input, key('?', { shiftKey: true }))).toBe(true);
      expect(press(shortcuts, input, key('s', { metaKey: true }))).toBe(true);
      expect(press(shortcuts, input, key('F5'))).toBe(true);
    }

    expect(ran).toEqual(types.flatMap(() => ['presetPicker', 'help', 'save', 'reload']));
  });

  it('yields bare keys to links, menu items, tabs, options, comboboxes and disclosure summaries', () => {
    const { shortcuts, ran } = service();
    const controls = [
      element('a', { href: '#' }),
      element('summary'),
      element('div', { role: 'menuitem', tabindex: '0' }),
      element('div', { role: 'tab', tabindex: '0' }),
      element('div', { role: 'option', tabindex: '0' }),
      element('div', { role: 'combobox', tabindex: '0' })
    ];

    for (const control of controls) {
      const space = key(' ', { code: 'Space' });
      expect(press(shortcuts, control, space)).toBe(false);
      expect(space.defaultPrevented).toBe(false);
      expect(press(shortcuts, control, key('z', { metaKey: true }))).toBe(true);
    }

    expect(ran).toEqual(controls.map(() => 'undo'));
  });

  it('runs bare-key shortcuts from what is not a control: an anchor without href, a panel, its text, the page', () => {
    const { shortcuts, ran } = service();
    const panel = element('section', { tabindex: '-1' });
    const text = panel.appendChild(document.createElement('p'));
    const targets = [element('a'), panel, text, document.body];
    for (const target of targets) {
      expect(press(shortcuts, target, key('p'))).toBe(true);
    }

    expect(ran).toEqual(targets.map(() => 'presetPicker'));
  });

  it('runs nothing for a key a control has already handled', () => {
    const { shortcuts, ran } = service();
    const knob = element('div', { tabindex: '0' });
    knob.addEventListener('keydown', event => event.preventDefault());

    expect(press(shortcuts, knob, key('t', { metaKey: true }))).toBe(false);
    expect(ran).toEqual([]);
  });

  it('consumes only the keys it has a handler for', () => {
    const { shortcuts } = service();
    const mute = key('m'); // a shortcut, but nothing on this page handles it
    expect(press(shortcuts, document.body, mute)).toBe(false);
    expect(mute.defaultPrevented).toBe(false);

    const unbound = key('j');
    expect(press(shortcuts, document.body, unbound)).toBe(false);
    expect(unbound.defaultPrevented).toBe(false);
  });

  it('leaves the rig alone behind a modal dialog: only the dialog toggles, mute and the clip reset run', () => {
    const ran: string[] = [];
    const shortcuts = new ShortcutService();
    const ids = ['bypass', 'global', 'channelNext', 'channelA', 'addBlock', 'presetPicker', 'undo', 'redo', 'reload'];
    const allowed = ['tuner', 'settings', 'help', 'mute', 'clearClips'];
    const all = [...ids, 'channelReset', 'scene2', 'save', ...allowed];
    shortcuts.register(Object.fromEntries(all.map(id => [id, () => ran.push(id)])));
    // An xUI dialog: a CDK overlay pane marked modal, focused itself (dialog-focus.ts) or on a button inside.
    const pane = element('div', { class: 'cdk-overlay-pane', role: 'dialog', 'aria-modal': 'true', tabindex: '-1' });
    const button = pane.appendChild(document.createElement('button'));

    const rig = [
      key(' ', { code: 'Space' }),
      key('g'),
      key('x'),
      key('a'),
      key('q'),
      key('p'),
      key('z', { metaKey: true }),
      key('z', { metaKey: true, shiftKey: true }),
      key('i', { metaKey: true }),
      key('2', { metaKey: true, code: 'Digit2' }),
      key('s', { metaKey: true }),
      key('F5')
    ];
    for (const event of rig) {
      expect(press(shortcuts, pane, event)).toBe(false);
      expect(event.defaultPrevented).toBe(false);
    }

    // Letters do not press a button, so without the dialog rule G would reach the rig from one.
    expect(press(shortcuts, button, key('g'))).toBe(false);
    // Focus left on the page behind (a dialog opened without taking it) changes nothing.
    expect(press(shortcuts, document.body, key('g'))).toBe(false);
    expect(ran).toEqual([]);

    const safe = [
      key('t', { metaKey: true }),
      key(',', { metaKey: true }),
      key('?', { shiftKey: true }),
      key('m'),
      key('k', { metaKey: true })
    ];
    for (const event of safe) {
      expect(press(shortcuts, pane, event)).toBe(true);
    }

    expect(ran).toEqual(allowed);

    // ⌘A still does not select the whole editor from a dialog.
    const selectAll = key('a', { metaKey: true });
    press(shortcuts, pane, selectAll);
    expect(selectAll.defaultPrevented).toBe(true);

    pane.remove();
    expect(press(shortcuts, document.body, key('g'))).toBe(true);
    expect(isDialogOpen(document.body)).toBe(false);
  });

  it('leaves ⌘C and ⌘X to the browser while text is selected, so an address or the token can be copied', () => {
    const ran: string[] = [];
    const shortcuts = new ShortcutService();
    shortcuts.register({ copy: () => ran.push('copy'), cut: () => ran.push('cut'), paste: () => ran.push('paste') });
    const address = element('span');
    address.textContent = 'http://192.168.1.20:5270';
    const selection = document.getSelection();
    if (!selection) {
      throw new Error('No selection in this DOM');
    }

    selection.selectAllChildren(address);
    expect(hasSelectedText(address)).toBe(true);
    for (const event of [key('c', { metaKey: true }), key('x', { ctrlKey: true })]) {
      expect(press(shortcuts, document.body, event)).toBe(false);
      expect(event.defaultPrevented).toBe(false);
    }

    expect(press(shortcuts, document.body, key('v', { metaKey: true }))).toBe(true);
    expect(ran).toEqual(['paste']);

    // Nothing selected: the keys are the grid's again.
    selection.removeAllRanges();
    expect(hasSelectedText(address)).toBe(false);
    const copy = key('c', { metaKey: true });
    expect(press(shortcuts, document.body, copy)).toBe(true);
    expect(copy.defaultPrevented).toBe(true);
    expect(press(shortcuts, document.body, key('x', { metaKey: true }))).toBe(true);
    expect(ran).toEqual(['paste', 'copy', 'cut']);
  });

  it('dispatches to the handler registered last; unregistering an older one leaves it in place', () => {
    const shortcuts = new ShortcutService();
    const ran: string[] = [];
    const page = shortcuts.register({ tuner: () => ran.push('page') });
    const dialog = shortcuts.register({ tuner: () => ran.push('dialog') });

    shortcuts.dispatch(key('t', { metaKey: true }));
    page();
    shortcuts.dispatch(key('t', { metaKey: true }));
    dialog();
    expect(shortcuts.dispatch(key('t', { metaKey: true }))).toBe(false);
    expect(ran).toEqual(['dialog', 'dialog']);
  });
});
