// Supply the isolation headers required by threaded WASM on GitHub Pages.
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('fetch', event => {
    const request = event.request;
    if (!request.url.startsWith(self.registration.scope) ||
        (request.cache === 'only-if-cached' && request.mode !== 'same-origin')) {
        return;
    }

    event.respondWith(fetchIsolated(request));
});

async function fetchIsolated(request) {
    const response = await fetch(request);
    if (!response.ok) {
        return response;
    }

    const headers = new Headers(response.headers);
    headers.set('Cross-Origin-Embedder-Policy', 'require-corp');
    if (request.mode === 'navigate') {
        headers.set('Cross-Origin-Opener-Policy', 'same-origin');
    }

    return new Response(response.body, {
        status: response.status,
        statusText: response.statusText,
        headers,
    });
}
