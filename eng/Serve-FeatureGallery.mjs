import { createServer } from 'node:http';
import { mkdtemp, mkdir, copyFile, stat } from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve, join, extname, sep } from 'node:path';
import { prepareGallery } from './Prepare-FeatureGallerySite.mjs';

// A static-host verification helper, not a SPA fallback. Only generated known
// entries return HTML; missing resources and unknown routes really return 404.
const [publishPath, portValue = '5235'] = process.argv.slice(2);
if (!publishPath) throw new Error('Usage: node eng/Serve-FeatureGallery.mjs <published-wwwroot> [port]');
const published = resolve(publishPath);
const port = Number(portValue);
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Invalid TCP port.');
const scratch = await mkdtemp(join(tmpdir(), 'Akbura-GalleryRouting-Host-'));
const configurations = [];
for (const prefix of ['/Akbura/Gallery/', '/Gallery/', '/']) {
    const directory = join(scratch, prefix.replaceAll('/', '_'));
    await mkdir(directory);
    await copyFile(join(published, 'index.html'), join(directory, 'index.html'));
    await prepareGallery(directory, `http://localhost:${port}${prefix}`);
    configurations.push({ prefix, directory });
}
const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2' };
const server = createServer(async (request, response) => {
    try {
        const requestUrl = new URL(request.url, `http://localhost:${port}`);
        const pathname = decodeURIComponent(requestUrl.pathname);
        if (configurations.some(config => config.prefix !== '/' && pathname === config.prefix.slice(0, -1))) {
            response.writeHead(301, { Location: requestUrl.pathname + '/' + requestUrl.search });
            response.end();
            return;
        }
        const { prefix, directory } = configurations.find(config => pathname.startsWith(config.prefix));
        const relative = pathname.slice(prefix.length) + (pathname.endsWith('/') ? 'index.html' : '');
        const prepared = resolve(directory, relative);
        const shared = resolve(published, relative);
        if (!prepared.startsWith(directory + sep) || !shared.startsWith(published + sep)) throw new Error('Invalid path.');
        let file = prepared;
        let info = await stat(file).catch(() => null);
        // Reuse immutable assets from the real publish without copying WASM for
        // each test prefix. HTML/config remain the actual prepared static files.
        if (!info && !['.html', '.js'].includes(extname(relative))) {
            file = shared;
            info = await stat(file).catch(() => null);
        } else if (!info && extname(relative) === '.js' && relative !== 'gallery-config.js') {
            file = shared;
            info = await stat(file).catch(() => null);
        }
        if (info?.isDirectory() && !pathname.endsWith('/')) {
            response.writeHead(301, { Location: requestUrl.pathname + '/' + requestUrl.search });
            response.end();
            return;
        }
        if (!info?.isFile()) {
            response.writeHead(404);
            response.end('Not found');
            console.error(`404 ${pathname}`);
            return;
        }
        response.writeHead(200, { 'Content-Type': types[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store', 'Cross-Origin-Opener-Policy': 'same-origin', 'Cross-Origin-Embedder-Policy': 'require-corp' });
        createReadStream(file).pipe(response);
    } catch {
        response.writeHead(400);
        response.end('Bad request');
    }
});
server.listen(port, 'localhost', () => console.log(`Gallery static test hosts on http://localhost:${port}/{,Gallery/,Akbura/Gallery/} (prepared HTML: ${scratch})`));
