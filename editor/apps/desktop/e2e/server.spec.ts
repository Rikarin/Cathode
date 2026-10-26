import { expect, test } from './fixtures';

// Which Server the desktop app uses: its own engine, or a remote one that may not be there.

test.describe('with a remote server that does not answer', () => {
  // Nothing listens on the discard port: every request is refused.
  test.use({ desktopSettings: { target: { mode: 'remote', baseUrl: 'http://127.0.0.1:9', token: 'secret' } } });

  test('the startup screen says so and leads back to this computer', async ({ app }) => {
    const window = await app.firstWindow();
    await expect(window.getByText('Cannot reach the Cathode server')).toBeVisible({ timeout: 30_000 });
    await expect(window.getByText('No answer from the Cathode server at http://127.0.0.1:9.')).toBeVisible();
    // The address can be changed right there, too.
    await expect(window.getByRole('textbox', { name: 'Server URL' })).toHaveValue('http://127.0.0.1:9');

    await window.getByRole('button', { name: 'Use this computer' }).click();
    await expect(window.getByRole('gridcell', { name: /^Input in1/ })).toBeVisible({ timeout: 90_000 });
    await window.keyboard.press('ControlOrMeta+Comma');
    await window.getByRole('tab', { name: 'Audio setup' }).click();
    await expect(window.getByRole('button', { name: 'Apply and restart engine' })).toBeVisible();
    await expect(window.getByRole('radio', { name: 'This computer' })).toBeChecked();
  });
});
