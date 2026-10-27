import { signal } from '@angular/core';

/**
 * Sends values by key with at most one request in flight per key: a value set while one is in flight waits, and only
 * the latest waiting value is sent when it returns. A dragged control so makes one request at a time, its answers
 * arrive in order, and the values in between are skipped rather than queued. `pending` holds what is sent or waiting
 * but not yet confirmed, so the control shows it instead of jumping back to the last confirmed value.
 */
export class Coalescer<T> {
  private readonly values = signal<ReadonlyMap<string, T>>(new Map());
  private readonly waiting = new Map<string, T>();
  private readonly inFlight = new Set<string>();

  constructor(private readonly send: (key: string, value: T) => Promise<unknown>) {}

  /** The value set for a key and not yet confirmed, if any. */
  pending(key: string): T | undefined {
    return this.values().get(key);
  }

  /** Sets a key's value; resolves once it (or a later value) is sent, or at once while a request is in flight. */
  set(key: string, value: T): Promise<void> {
    this.values.update(values => new Map(values).set(key, value));
    this.waiting.set(key, value);
    return this.flush(key);
  }

  private async flush(key: string): Promise<void> {
    if (this.inFlight.has(key)) {
      return; // sent when the request in flight returns
    }

    this.inFlight.add(key);
    try {
      while (this.waiting.has(key)) {
        const value = this.waiting.get(key) as T;
        this.waiting.delete(key);
        try {
          await this.send(key, value);
        } catch {
          // the sender reports its own failures; the next value is still sent
        }
      }
    } finally {
      this.inFlight.delete(key);
      this.values.update(values => {
        const next = new Map(values);
        next.delete(key);
        return next;
      });
    }
  }
}
