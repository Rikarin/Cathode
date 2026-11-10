/**
 * A new random id, as the Server makes them (32 hex digits). Built on crypto.getRandomValues: crypto.randomUUID
 * needs a secure context, and the editor a rig serves over the LAN (plain http) is not one.
 */
export function newId(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}
