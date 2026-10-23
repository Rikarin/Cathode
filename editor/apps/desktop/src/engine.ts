import { app } from 'electron';
import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';

export interface Command {
    command: string;
    args: string[];
}

/**
 * Where cathode-host and cathode-server are. CATHODE_ENGINE_DIR overrides; a packaged app carries them in
 * resources/engine; a development checkout uses the Release builds under engine/ (run through `dotnet`).
 */
function locate(name: 'cathode-host' | 'cathode-server'): Command {
    const native = process.platform === 'win32' ? `${name}.exe` : name;
    const override = process.env['CATHODE_ENGINE_DIR'];
    const packaged = app.isPackaged ? join(process.resourcesPath, 'engine') : null;
    for (const dir of [override, packaged].filter((d): d is string => !!d)) {
        if (existsSync(join(dir, native))) {
            return { command: join(dir, native), args: [] };
        }

        if (existsSync(join(dir, `${name}.dll`))) {
            return { command: 'dotnet', args: [join(dir, `${name}.dll`)] };
        }
    }

    const project = name === 'cathode-host' ? 'Cathode.Host' : 'Cathode.Server';
    const checkout = resolve(__dirname, '../../../../engine/src', project, 'bin/Release/net10.0', `${name}.dll`);
    if (existsSync(checkout)) {
        return { command: 'dotnet', args: [checkout] };
    }

    throw new Error(
        `${name} not found. Build the engine (cd engine && dotnet build -c Release) or set CATHODE_ENGINE_DIR.`
    );
}

export const engine = {
    host: () => locate('cathode-host'),
    server: () => locate('cathode-server')
};
