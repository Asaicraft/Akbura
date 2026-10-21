const subscribers = new Map();
let nextSubscription = 1;

export function resolveGalleryBaseUrl(configured, bootModuleUrl) {
    const boot = new URL(bootModuleUrl);
    const base = configured == null ? new URL('./', boot) : new URL(configured);
    if (!['http:', 'https:'].includes(base.protocol) || base.origin !== boot.origin ||
        base.username || base.password || base.search || base.hash) {
        throw new Error('Expected a same-origin HTTP(S) gallery base URL without credentials, query or fragment.');
    }
    if (!base.pathname.endsWith('/')) base.pathname += '/';
    return base.href;
}

export function getCurrentUrl() { return window.location.href; }
export function pushUrl(url) { window.history.pushState(null, '', url); }
export function replaceUrl(url) { window.history.replaceState(null, '', url); }

function onPopState() {
    for (const callback of [...subscribers.values()]) callback(window.location.href);
}

export function subscribe(callback) {
    if (typeof callback !== 'function') throw new TypeError('Expected a URL callback.');
    if (subscribers.size === 0) window.addEventListener('popstate', onPopState);
    const id = nextSubscription++;
    subscribers.set(id, callback);
    return id;
}

export function unsubscribe(id) {
    subscribers.delete(id);
    if (subscribers.size === 0) window.removeEventListener('popstate', onPopState);
}
