import { join } from 'node:path';
import { expect, freePort, test } from './fixtures';
import { connection, hosts, mainPid, servers, socketExists } from './processes';
import { packaged } from './target';

// The packaged app, as an installer puts it on a user's machine: its own Electron, the editor inside the asar archive
// and the published engine in resources/engine. The other specs find the same paths through a development checkout
// unless they too run with CATHODE_PACKAGED=1, so this one checks what only a packaged app does.

test.skip(!packaged, 'needs the packaged app: run pnpm dist, then set CATHODE_PACKAGED=1');

// The first start of a freshly built app is slow: the OS checks the new bundle (half a minute on macOS before the
// window shows) and the single-file Server unpacks itself.
test.describe.configure({ timeout: 300_000 });

const Token = 'e2e-packaged-token-0123456789';

let port = 0;
test.beforeAll(async () => {
  port = await freePort();
});
test.use({
  // On the network too, which is when the Server is given the editor's files to serve.
  // eslint-disable-next-line no-empty-pattern -- Playwright reads a fixture's dependencies from its first parameter's destructuring; this one has none
  desktopSettings: async ({}, use) => use({ network: { enabled: true, port, token: Token } })
});

test('it starts the engine it carries, serves its own editor and stops cleanly', async ({ app, window }) => {
  expect(await app.evaluate(({ app: electron }) => electron.isPackaged)).toBe(true);
  const resources = await app.evaluate(() => process.resourcesPath);
  const pid = await mainPid(app);

  // The engine is the one in resources/engine, started as programs of their own (no `dotnet`, no checkout).
  const engine = join(resources, 'engine');
  const [host, ...otherHosts] = hosts(pid);
  const [server, ...otherServers] = servers(pid);
  expect(otherHosts).toEqual([]);
  expect(otherServers).toEqual([]);
  expect(host).toContain(join(engine, 'cathode-host'));
  expect(server).toContain(join(engine, 'cathode-server'));
  for (const line of [host, server]) {
    expect(line).not.toMatch(/\.dll\b/);
  }

  // The window (the fixture waited for the grid) loaded the editor from the archive and reached the Server, and the
  // Server reached the host.
  const { baseUrl, token } = await connection(window);
  const system = await fetch(`${baseUrl}/api/v1/system`, { headers: { Authorization: `Bearer ${token}` } });
  expect(system.status).toBe(200);
  expect(await system.json()).toMatchObject({ engine: { connected: true } });

  // The Server cannot read the archive: it serves the editor from the unpacked copy beside it.
  expect(server).toContain(join(resources, 'app.asar.unpacked', 'editor'));
  const page = await fetch(`http://127.0.0.1:${port}/`);
  expect(page.status).toBe(200);
  expect(await page.text()).toContain('<meta name="cathode-server" content="same-origin">');

  // Quitting leaves nothing running.
  await app.close();
  expect(hosts(pid)).toEqual([]);
  expect(servers(pid)).toEqual([]);
  expect(socketExists(pid)).toBe(false);
});
