import { access, readFile, readdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { brotliCompressSync, gzipSync } from 'node:zlib';

const [directory] = process.argv.slice(2);
if (!directory) {
    throw new Error('Usage: node eng/Prepare-PlaygroundSite.mjs <published-wwwroot>');
}

const framework = join(directory, '_framework');
const assets = await readdir(framework);
if (!assets.includes('dotnet.js') ||
    !assets.some(name => /^dotnet\.native(?:\.[^.]+)?\.wasm$/.test(name)) ||
    !assets.some(name => /^dotnet\.native\.worker(?:\.[^.]+)?\.mjs$/.test(name))) {
    throw new Error('Playground is missing the threaded .NET WebAssembly runtime.');
}
await Promise.all([
    access(join(directory, 'pages-isolation.js')),
    access(join(directory, 'mobile-text-input.js')),
]);

const index = join(directory, 'index.html');
const html = await readFile(index, 'utf8');
const attribute = 'data-pages-isolation="false"';
if (!html.includes(attribute)) {
    throw new Error('Playground index is missing its Pages isolation setting.');
}

// Keep the SDK's precompressed index in sync with the Pages entry point.
const prepared = html.replace(attribute, 'data-pages-isolation="true"');
await Promise.all([
    writeFile(index, prepared),
    writeFile(index + '.gz', gzipSync(prepared)),
    writeFile(index + '.br', brotliCompressSync(prepared)),
]);
