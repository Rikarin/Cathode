import type { Bank, PresetSummary } from '@cathode/api-client';

export const SlotCount = 128;

/**
 * A bank being edited: its slots, null where empty. (The Server stores empty slots as null; the generated type says
 * strings, as OpenAPI drops the nullable element.)
 */
export interface WorkBank {
  id: string;
  name: string;
  slots: (string | null)[];
  /** The factory bank: read-only, its presets copied or placed into the user's banks. */
  factory?: boolean | null;
}

/** A slot of a bank: which bank, which slot (0-based). */
export interface SlotRef {
  bank: string;
  slot: number;
}

/** A preset to create on Save: a copy of `source` under a new id. */
export interface PendingCopy {
  id: string;
  source: string;
  name: string;
}

/** Metadata to change on Save; only the fields set change. */
export interface PendingMeta {
  name?: string;
  color?: string;
  favorite?: boolean;
}

/** The selection after a click on `index`: plain picks one, ⌘ toggles it, ⇧ extends from the anchor. */
export function nextSelection(
  selected: ReadonlySet<number>,
  anchor: number | null,
  index: number,
  keys: { shift: boolean; toggle: boolean }
): { selected: Set<number>; anchor: number } {
  if (keys.shift && anchor !== null) {
    const [from, to] = anchor <= index ? [anchor, index] : [index, anchor];
    const range = new Set(keys.toggle ? selected : []);
    for (let i = from; i <= to; i++) {
      range.add(i);
    }

    return { selected: range, anchor };
  }

  if (keys.toggle) {
    const next = new Set(selected);
    if (!next.delete(index)) {
      next.add(index);
    }

    return { selected: next, anchor: index };
  }

  return { selected: new Set([index]), anchor: index };
}

/**
 * Manage Presets' changes, held until Save: banks are edited as working copies (swap, move, copy between slots and
 * banks), and renames, metadata, copies and deletions are queued. Nothing reaches the Server until {@link plan} is
 * carried out; Close discards. Every slot and preset touched is marked changed. The factory bank and its presets are
 * read-only: nothing lands in its slots, and its presets are not renamed or deleted (their colour and favourite can
 * change); they can be placed in, or copied into, the user's banks.
 */
export class ManageSession {
  readonly banks: WorkBank[];
  readonly presets: Map<string, PresetSummary>;
  readonly copies: PendingCopy[] = [];
  readonly meta = new Map<string, PendingMeta>();
  readonly deleted = new Set<string>();
  /** `bank:slot` of every slot changed. */
  readonly changedSlots = new Set<string>();
  private nextCopy = 0;

  constructor(
    banks: readonly Bank[],
    presets: readonly PresetSummary[],
    private readonly newId: () => string = () => crypto.randomUUID().replaceAll('-', '')
  ) {
    this.banks = banks.map(b => ({
      ...b,
      slots: Array.from({ length: SlotCount }, (_, i) => b.slots[i] ?? null)
    }));
    this.presets = new Map(presets.map(p => [p.id, p]));
  }

  get dirty(): boolean {
    return this.changedSlots.size > 0 || this.copies.length > 0 || this.meta.size > 0 || this.deleted.size > 0;
  }

  bank(id: string): WorkBank {
    const bank = this.banks.find(b => b.id === id);
    if (!bank) {
      throw new Error(`No bank '${id}'.`);
    }

    return bank;
  }

  /** Whether a bank is the read-only factory bank. */
  isFactoryBank(id: string): boolean {
    return this.banks.find(b => b.id === id)?.factory ?? false;
  }

  /** Whether a preset is one of the factory's (read-only). */
  isFactory(id: string): boolean {
    return this.presets.get(id)?.factory ?? false;
  }

  at(ref: SlotRef): string | null {
    return this.bank(ref.bank).slots[ref.slot] ?? null;
  }

  /** The name a preset will have after Save (a pending copy's or rename's). */
  nameOf(id: string): string {
    return this.meta.get(id)?.name ?? this.copies.find(c => c.id === id)?.name ?? this.presets.get(id)?.name ?? id;
  }

  isChanged(ref: SlotRef): boolean {
    const id = this.at(ref);
    return (
      this.changedSlots.has(`${ref.bank}:${ref.slot}`) ||
      (id !== null && (this.meta.has(id) || this.copies.some(c => c.id === id)))
    );
  }

  /** Drag: the two slots trade places. */
  swap(a: SlotRef, b: SlotRef): void {
    const first = this.at(a);
    this.put(a, this.at(b));
    this.put(b, first);
  }

  /** ⌘-drag: the preset goes to the target slot; the source slot empties. */
  move(from: SlotRef, to: SlotRef): void {
    const id = this.at(from);
    if (id === null || (from.bank === to.bank && from.slot === to.slot)) {
      return;
    }

    this.put(from, null);
    this.put(to, id);
  }

  /** ⇧-drag: a copy of the preset (a new one, made on Save) goes to the target slot. */
  copy(from: SlotRef, to: SlotRef): void {
    const id = this.at(from);
    if (id !== null && !this.isFactoryBank(to.bank)) {
      this.put(to, this.duplicate(id));
    }
  }

  /** Puts a library preset in a slot (dragged from the preset list). */
  assign(id: string, to: SlotRef): void {
    this.put(to, id);
  }

  /** Copies of the presets in `refs`, each into the next empty slot of the same bank. */
  duplicateSlots(refs: readonly SlotRef[]): void {
    for (const ref of refs.filter(r => !this.isFactoryBank(r.bank))) {
      const id = this.at(ref);
      const bank = this.bank(ref.bank);
      const empty = bank.slots.findIndex((s, i) => s === null && i > ref.slot);
      if (id !== null && empty >= 0) {
        this.put({ bank: ref.bank, slot: empty }, this.duplicate(id));
      }
    }
  }

  /** Adds a bank the page has just stored on the Server (banks are created, renamed and deleted at once). */
  addBank(bank: Bank): WorkBank {
    const work: WorkBank = { ...bank, slots: Array.from({ length: SlotCount }, (_, i) => bank.slots[i] ?? null) };
    this.banks.push(work);
    return work;
  }

  /**
   * Renames a bank in the session. The page renames it on the Server at once; this keeps the working copy in step, so a
   * later Save of its slots does not put the old name back.
   */
  renameBank(id: string, name: string): void {
    this.bank(id).name = name;
  }

  /**
   * Drops a bank from the session with its pending slot changes, after the page has deleted it on the Server. Its
   * presets stay in the library.
   */
  deleteBank(id: string): void {
    this.banks.splice(this.banks.indexOf(this.bank(id)), 1);
    for (const key of [...this.changedSlots].filter(k => k.startsWith(`${id}:`))) {
      this.changedSlots.delete(key);
    }
  }

  rename(id: string, name: string): void {
    if (this.isFactory(id)) {
      return;
    }

    const copy = this.copies.find(c => c.id === id);
    if (copy) {
      copy.name = name;
    } else {
      this.meta.set(id, { ...this.meta.get(id), name });
    }
  }

  setMeta(id: string, change: Omit<PendingMeta, 'name'>): void {
    this.meta.set(id, { ...this.meta.get(id), ...change });
  }

  /** Deletes presets on Save; they leave every slot now. A pending copy is simply dropped. */
  delete(ids: readonly string[]): void {
    for (const id of ids.filter(i => !this.isFactory(i))) {
      const copy = this.copies.findIndex(c => c.id === id);
      if (copy >= 0) {
        this.copies.splice(copy, 1);
      } else {
        this.deleted.add(id);
        this.meta.delete(id);
      }

      for (const bank of this.banks) {
        bank.slots.forEach((slot, i) => slot === id && this.put({ bank: bank.id, slot: i }, null));
      }
    }
  }

  /**
   * What Save does, in an order the Server accepts: copies first (bank slots must name existing presets), then
   * renames and metadata, then the changed banks, and deletions last (a preset in a bank cannot be deleted).
   */
  plan(): {
    copies: PendingCopy[];
    meta: [string, PendingMeta][];
    banks: WorkBank[];
    deletes: string[];
  } {
    const changedBanks = new Set([...this.changedSlots].map(key => key.slice(0, key.lastIndexOf(':'))));
    return {
      copies: [...this.copies],
      meta: [...this.meta.entries()],
      banks: this.banks.filter(b => changedBanks.has(b.id)).map(b => ({ ...b, slots: trimSlots(b.slots) })),
      deletes: [...this.deleted]
    };
  }

  private duplicate(source: string): string {
    const original = this.copies.find(c => c.id === source)?.source ?? source;
    const id = this.newId() || `copy-${this.nextCopy++}`;
    this.copies.push({ id, source: original, name: `${this.nameOf(source)} copy` });
    return id;
  }

  /** Sets a slot; the factory bank's stay as they are. */
  private put(ref: SlotRef, id: string | null): void {
    const bank = this.bank(ref.bank);
    if (!bank.factory && bank.slots[ref.slot] !== id) {
      bank.slots[ref.slot] = id;
      this.changedSlots.add(`${ref.bank}:${ref.slot}`);
    }
  }
}

/** A bank's slots without the empty tail, as the Server stores them. */
export function trimSlots(slots: readonly (string | null | undefined)[]): (string | null)[] {
  const trimmed = slots.map(s => s ?? null);
  while (trimmed.length > 0 && trimmed[trimmed.length - 1] === null) {
    trimmed.pop();
  }

  return trimmed;
}
