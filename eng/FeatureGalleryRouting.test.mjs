import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, rm, mkdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gunzipSync, brotliDecompressSync } from 'node:zlib';
import { prepareGallery, registeredRoutes, resolveSiteBaseUrl, applicationBase } from './Prepare-FeatureGallerySite.mjs';

const galleryIndex = new URL('../src/Akbura.FeatureGallery/Akbura.FeatureGallery.Browser/wwwroot/index.html', import.meta.url);

test('dev-server deep links resolve the real stylesheet and boot module from the application root', async () => {
    const html = await readFile(galleryIndex, 'utf8');
    const base = html.match(/<base\b[^>]*\bhref="([^"]+)"[^>]*>/i);
    const stylesheet = html.match(/<link\b[^>]*\brel="stylesheet"[^>]*\bhref="([^"]+)"/i);
    const bootModule = html.match(/\bimport\s+"([^"]+)"/);
    assert.ok(base && stylesheet && bootModule, 'Expected the actual HTML bootstrap and stylesheet.');
    assert.ok(base.index < stylesheet.index && base.index < html.indexOf('<script type="importmap">'));
    for (const location of ['https://localhost:7169/utility-variants/', 'https://localhost:7169/commands/?q=1#example']) {
        const documentBase = new URL(base[1], location);
        assert.equal(new URL(stylesheet[1], documentBase).href, 'https://localhost:7169/app.css');
        assert.equal(new URL(bootModule[1], documentBase).href, 'https://localhost:7169/main.js');
    }
});

test('Pages base keeps project prefix, but not on custom domains or user sites', () => {
    assert.equal(resolveSiteBaseUrl('Asaicraft/Akbura', 'Asaicraft', ''), 'https://asaicraft.github.io/Akbura/');
    assert.equal(resolveSiteBaseUrl('Asaicraft/Akbura', 'Asaicraft', 'example.com'), 'https://example.com/');
    assert.equal(resolveSiteBaseUrl('Asaicraft/Asaicraft.github.io', 'Asaicraft', ''), 'https://asaicraft.github.io/');
    for (const url of ['file:///tmp/', 'https://user:pass@host/', 'https://host/?x', 'https://host/#x'])
        assert.throws(() => applicationBase(url));
});

test('entry pages are generated from actual App routes, with assets anchored to the configured base', async () => {
    const temp = await mkdtemp(join(tmpdir(), 'akbura-routing-'));
    try {
        const source = await readFile(new URL('../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Components/App.akbura', import.meta.url), 'utf8');
        const expected = registeredRoutes(source);
        assert.ok(expected.includes('/commands'));
        for (const prefix of ['/', '/Gallery/', '/Akbura/Gallery/']) {
            const directory = join(temp, prefix.replaceAll('/', '_'));
            await mkdir(directory);
            await writeFile(join(directory, 'index.html'), await readFile(galleryIndex, 'utf8'));
            const base = `http://localhost:5235${prefix}`;
            assert.deepEqual(await prepareGallery(directory, base), expected);
            const root = await readFile(join(directory, 'index.html'), 'utf8');
            assert.equal(gunzipSync(await readFile(join(directory, 'index.html.gz'))).toString(), root);
            assert.equal(brotliDecompressSync(await readFile(join(directory, 'index.html.br'))).toString(), root);
            assert.equal((root.match(/<base /g) ?? []).length, 1);
            assert.ok(root.indexOf(`<base href="${base}">`) < root.indexOf('<link'));
            for (const route of expected) assert.equal(await readFile(join(directory, route.slice(1), 'index.html'), 'utf8'), root);
            const config = await import(`data:text/javascript,${encodeURIComponent(await readFile(join(directory, 'gallery-config.js'), 'utf8'))}`);
            assert.equal(config.galleryConfig.baseUrl, base);
            assert.equal(gunzipSync(await readFile(join(directory, 'gallery-config.js.gz'))).toString(), await readFile(join(directory, 'gallery-config.js'), 'utf8'));
            assert.equal(brotliDecompressSync(await readFile(join(directory, 'gallery-config.js.br'))).toString(), await readFile(join(directory, 'gallery-config.js'), 'utf8'));
            assert.equal(new URL('_framework/dotnet.js', config.galleryConfig.baseUrl).pathname, `${prefix}_framework/dotnet.js`);
        }
    } finally { await rm(temp, { recursive: true, force: true }); }
});

test('manifest extraction rejects missing, dynamic, duplicate and unsafe Page routes', () => {
    for (const source of ['', '<Page Url={dynamic}>', '<Page Url="/a"/><Page Url="/A"/>', '<Page Url="/../bad">', '<Page Url="https://host/">'])
        assert.throws(() => registeredRoutes(source));
});

test('real browser bridge has one popstate listener, history operations never synthesize popstate, and disposal removes callbacks', async () => {
    const source = await readFile(new URL('../src/Akbura.FeatureGallery/Akbura.FeatureGallery.Browser/wwwroot/browser-router.js', import.meta.url), 'utf8');
    const bridge = await import(`data:text/javascript,${encodeURIComponent(source)}`);
    const listeners = new Set();
    const history = [];
    const previous = globalThis.window;
    globalThis.window = {
        location: { href: 'http://localhost/Gallery/commands/' },
        history: { pushState: (_, __, url) => history.push(['push', url]), replaceState: (_, __, url) => history.push(['replace', url]) },
        addEventListener: (type, callback) => { assert.equal(type, 'popstate'); listeners.add(callback); },
        removeEventListener: (_, callback) => listeners.delete(callback),
    };
    try {
        assert.equal(bridge.resolveGalleryBaseUrl(null, 'http://localhost/Akbura/Gallery/main.123.js'), 'http://localhost/Akbura/Gallery/');
        assert.equal(bridge.resolveGalleryBaseUrl('http://localhost/apps/gallery', 'http://localhost/apps/gallery/main.js'), 'http://localhost/apps/gallery/');
        for (const url of ['https://external/', 'file:///tmp/', 'http://localhost/?q', 'http://user@localhost/'])
            assert.throws(() => bridge.resolveGalleryBaseUrl(url, 'http://localhost/main.js'));
        const calls = [];
        const first = bridge.subscribe(url => calls.push(url));
        const second = bridge.subscribe(url => calls.push(`second:${url}`));
        assert.equal(listeners.size, 1);
        bridge.pushUrl('/Gallery/overview/');
        bridge.replaceUrl('/Gallery/commands/');
        assert.equal(calls.length, 0);
        assert.deepEqual(history.map(entry => entry[0]), ['push', 'replace']);
        [...listeners][0]();
        assert.deepEqual(calls, [window.location.href, `second:${window.location.href}`]);
        bridge.unsubscribe(first);
        assert.equal(listeners.size, 1);
        bridge.unsubscribe(second);
        assert.equal(listeners.size, 0);
        const next = bridge.subscribe(() => {});
        assert.equal(listeners.size, 1);
        bridge.unsubscribe(next);
        assert.equal(listeners.size, 0);
    } finally { globalThis.window = previous; }
});
