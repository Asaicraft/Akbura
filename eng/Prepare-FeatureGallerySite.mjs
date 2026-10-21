import { readFile, writeFile, mkdir, appendFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { gzipSync, brotliCompressSync } from 'node:zlib';

const appSource = fileURLToPath(new URL('../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Components/App.akbura', import.meta.url));

export function applicationBase(value) {
    const url = new URL(value);
    if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password || url.search || url.hash)
        throw new Error('Expected an absolute HTTP(S) application base URL.');
    if (!url.pathname.endsWith('/')) url.pathname += '/';
    return url;
}

export function resolveSiteBaseUrl(repository, owner, customDomain) {
    if (customDomain?.trim()) return applicationBase(`https://${customDomain.trim()}/`).href;
    const repo = repository.split('/');
    if (repo.length !== 2 || !repo.every(part => /^[\w.-]+$/.test(part)) || !/^[\w-]+$/.test(owner))
        throw new Error('Expected GitHub owner/repository and owner names.');
    const host = `${owner.toLowerCase()}.github.io`;
    return applicationBase(`https://${host}/${repo[1].toLowerCase() === host ? '' : `${repo[1]}/`}`).href;
}

export function registeredRoutes(source) {
    // Deployment manifest extraction, not a second route list. Fail closed if a
    // Page registration stops using a literal slug; do not silently omit it.
    const tags = [...source.matchAll(/<Page\b([^>]*?)>/gs)];
    if (!tags.length) throw new Error('No Page registrations found in App.akbura.');
    const routes = tags.map(([, attributes]) => {
        const match = attributes.match(/^\s+Url\s*=\s*"(\/[A-Za-z0-9_-]+(?:\/[A-Za-z0-9_-]+)*)"\s*\/?\s*$/);
        if (!match) throw new Error(`Expected a literal Page Url slug: ${attributes}`);
        return match[1].toLowerCase();
    });
    if (new Set(routes).size !== routes.length) throw new Error('Duplicate normalized Page routes.');
    return routes;
}

export async function prepareGallery(directory, baseUrl, sourcePath = appSource) {
    const base = applicationBase(baseUrl).href;
    const routes = registeredRoutes(await readFile(sourcePath, 'utf8'));
    const indexPath = join(directory, 'index.html');
    let html = await readFile(indexPath, 'utf8');
    if (!/<head\b[^>]*>/i.test(html)) throw new Error('Gallery index has no head element.');
    const escaped = base.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
    html = html.replace(/<base\b[^>]*>\s*/gi, '')
        .replace(/(<head\b[^>]*>)/i, `$1\r\n    <base href="${escaped}">`);
    await writeAsset(indexPath, html);
    // Serialize configuration, never interpolate shell text into JavaScript.
    await writeAsset(join(directory, 'gallery-config.js'), `export const galleryConfig = Object.freeze(${JSON.stringify({ baseUrl: base })});\r\n`);
    for (const route of routes) {
        const entry = join(directory, route.slice(1));
        await mkdir(entry, { recursive: true });
        await writeAsset(join(entry, 'index.html'), html);
    }
    return routes;
}

async function writeAsset(path, content) {
    // Publish includes precompressed originals. Regenerate them as well, or a
    // compression-aware host could serve stale base/configuration content.
    await Promise.all([
        writeFile(path, content),
        writeFile(path + '.gz', gzipSync(content)),
        writeFile(path + '.br', brotliCompressSync(content)),
    ]);
}

async function main() {
    if (process.argv[2] === '--github-environment') {
        const site = resolveSiteBaseUrl(process.env.GITHUB_REPOSITORY, process.env.GITHUB_REPOSITORY_OWNER, process.env.CUSTOM_DOMAIN);
        await appendFile(process.env.GITHUB_ENV, `ssg_base_href=${site}\nGALLERY_BASE_URL=${new URL('Gallery/', site).href}\n`);
        return;
    }
    const [directory, baseUrl] = process.argv.slice(2);
    if (!directory || !baseUrl) throw new Error('Usage: node eng/Prepare-FeatureGallerySite.mjs <published-wwwroot> <absolute-gallery-base-url>');
    const routes = await prepareGallery(resolve(directory), baseUrl);
    console.log(`Prepared ${routes.length} gallery entry pages at ${applicationBase(baseUrl).href}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) await main();
