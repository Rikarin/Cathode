import {
  type ElectronApplication,
  type Locator,
  type Page,
  test as base,
  _electron as electron,
  expect
} from '@playwright/test';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const main = resolve(__dirname, '../../../dist/apps/desktop/main.js');

/** The editor as a user sees it: the grid, the block picker and the selected block's page. */
export class Editor {
  constructor(readonly window: Page) {}

  get grid(): Locator {
    return this.window.getByRole('grid', { name: 'Signal grid' });
  }

  /** A grid cell by its zero-based position. */
  cell(row: number, column: number): Locator {
    return this.window.getByTestId(`cell-${row}-${column}`);
  }

  /** A block's grid cell by its accessible name, which starts with the type name and the block id. */
  block(name: string, id: string): Locator {
    return this.window.getByRole('gridcell', { name: new RegExp(`^${escape(name)} ${escape(id)}\\b`) });
  }

  /** Adds a block through the picker: double-click an empty cell, search, Enter. */
  async addBlock(row: number, column: number, search: string): Promise<void> {
    await this.cell(row, column).dblclick();
    await this.window.getByRole('combobox', { name: 'Search blocks' }).fill(search);
    await this.window.keyboard.press('Enter');
    await expect(this.window.getByRole('dialog', { name: 'Add a block' })).toBeHidden();
    await expect(this.cell(row, column)).not.toHaveAccessibleName(/^(Empty|Shunt)/);
  }

  /** A knob on the block page, by its exact name. */
  knob(name: string): Locator {
    return this.window.getByRole('slider', { name, exact: true });
  }

  /** Types a value into a knob, the way a user does: double-click, type, Enter. */
  async setKnob(name: string, value: string): Promise<void> {
    await this.knob(name).dblclick();
    const field = this.window.getByRole('textbox', { name: `${name} value` });
    await field.fill(value);
    await field.press('Enter');
  }

  /** A radio in one of the editor's segmented controls (Channel, Scene, A/B compare). */
  segment(group: string, option: string): Locator {
    return this.window.getByRole('radiogroup', { name: group }).getByRole('radio', { name: option, exact: true });
  }

  /** Picks an option of a select by the select's accessible name. */
  async choose(select: string, option: string): Promise<void> {
    await this.window.getByRole('combobox', { name: select, exact: true }).click();
    await this.window.getByRole('option', { name: option, exact: true }).click();
  }
}

/** Each test gets a fresh app: throwaway settings and data, its own sidecars (null audio). */
export const test = base.extend<{ app: ElectronApplication; window: Page; editor: Editor }>({
  app: async ({}, use) => {
    const scratch = mkdtempSync(join(tmpdir(), 'cathode-e2e-'));
    const app = await electron.launch({
      args: [main],
      env: {
        ...process.env,
        CATHODE_USER_DATA: join(scratch, 'user-data'),
        CATHODE_DATA_DIR: join(scratch, 'server-data')
      }
    });
    await use(app);
    await app.close();
    rmSync(scratch, { recursive: true, force: true });
  },
  window: async ({ app }, use) => {
    const window = await app.firstWindow();
    // First start: the sidecars have to come up and the editor has to connect.
    await expect(window.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible({ timeout: 90_000 });
    await use(window);
  },
  editor: async ({ window }, use) => {
    await use(new Editor(window));
  }
});

export { expect };

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
