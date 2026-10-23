import { Injectable, inject } from '@angular/core';
import { ApiError } from '@cathode/api-client';
import { XuiToastService } from '@xui/toast';

/** User-facing feedback for actions: failures show the Server's reason. */
@Injectable({ providedIn: 'root' })
export class Notify {
    private readonly toaster = inject(XuiToastService);

    success(message: string): void {
        this.toaster.show({ message, color: 'success' });
    }

    error(error: unknown): void {
        const message =
            error instanceof ApiError
                ? [error.message, ...error.errors].join(' ')
                : error instanceof Error
                  ? error.message
                  : String(error);
        this.toaster.show({ message, color: 'error', timeout: 8_000 });
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
