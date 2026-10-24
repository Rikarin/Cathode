import { ShortcutService, Shortcuts, findShortcut, isTyping, shortcutKeys } from './shortcuts';

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
    expect(findShortcut(key('g', { ctrlKey: true }))?.id).toBe('goTo');
    expect(findShortcut(key('g'))?.id).toBe('global');
    expect(findShortcut(key('F5'))?.id).toBe('reload');
    expect(findShortcut(key('3', { metaKey: true, code: 'Digit3' }))?.id).toBe('scene3');
    expect(findShortcut(key('c'))?.id).toBe('channelC');
    expect(findShortcut(key('x'))?.id).toBe('channelNext');
    expect(findShortcut(key('x', { metaKey: true }))?.id).toBe('cut');
    expect(findShortcut(key(' ', { code: 'Space' }))?.id).toBe('bypass');
    expect(findShortcut(key(' ', { code: 'Space', shiftKey: true }))?.id).toBe('engageAll');
    expect(findShortcut(key(' ', { code: 'Space', shiftKey: true, metaKey: true }))?.id).toBe('bypassAll');
    expect(findShortcut(key('b'))?.id).toBe('bypassAlias');
    expect(findShortcut(key('B', { shiftKey: true }))?.id).toBe('channelB');
    expect(findShortcut(key('b', { metaKey: true }))?.id).toBe('saveBlock');
    expect(findShortcut(key('d', { metaKey: true }))?.id).toBe('disconnect');
    expect(findShortcut(key('u', { metaKey: true }))?.id).toBe('shunt');
    expect(findShortcut(key('q'))?.id).toBe('addBlock');
    expect(findShortcut(key('F1'))?.id).toBe('blockInfo');
    expect(findShortcut(key('t', { metaKey: true }))?.id).toBe('tuner');
    expect(findShortcut(key('y', { metaKey: true }))?.id).toBe('redoAlias');
    expect(findShortcut(key('z', { metaKey: true, shiftKey: true }))?.id).toBe('redo');
    expect(findShortcut(key('?', { shiftKey: true }))?.id).toBe('help');
  });

  it('reads as the platform writes it', () => {
    const saveAs = Shortcuts.find(s => s.id === 'saveAs')!;
    expect(shortcutKeys(saveAs, true)).toEqual(['⌘', '⇧', 'S']);
    expect(shortcutKeys(saveAs, false)).toEqual(['Ctrl', '⇧', 'S']);
    expect(
      shortcutKeys(
        Shortcuts.find(s => s.id === 'scene8')!,
        true
      )
    ).toEqual(['⌘', '8']);
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

  it('shows Block Info while ⌥ is held', () => {
    const service = new ShortcutService();
    service.dispatch(key('Alt', { altKey: true }));
    expect(service.altHeld()).toBe(true);
    service.keyUp(new KeyboardEvent('keyup', { key: 'Alt' }));
    expect(service.altHeld()).toBe(false);
  });
});
