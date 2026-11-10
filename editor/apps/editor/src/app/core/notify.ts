import { Injectable, inject } from '@angular/core';
import { ApiError } from '@cathode/api-client';
import { XuiToastService } from '@xui/toast';

/** A failure as the user reads it: the Server's reason with its details, or the error's own message. */
export function errorMessage(error: unknown): string {
  return error instanceof ApiError
    ? [error.message, ...error.errors].join(' ')
    : error instanceof Error
      ? error.message
      : String(error);
}

/** User-facing feedback for actions: failures show the Server's reason. */
@Injectable({ providedIn: 'root' })
export class Notify {
  private readonly toaster = inject(XuiToastService);

  success(message: string): void {
    this.toaster.show({ message, color: 'success' });
  }

  error(error: unknown): void {
    this.toaster.show({ message: errorMessage(error), color: 'error', timeout: 8_000 });
  }

  /** Runs an action and reports its failure; resolves to undefined when it failed. */
  async run<T>(action: () => Promise<T>): Promise<T | undefined> {
    try {
      return await action();
    } catch (error) {
      this.error(error);
      return undefined;
    }
  }
}
