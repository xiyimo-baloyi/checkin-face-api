// African Ideas Attendance Service Worker
// Only handles same-origin static assets; Blazor navigations and /admin /uploads bypass the SW
// to avoid synthetic error responses and broken dynamic loads.
const CACHE_NAME = 'african-ideas-v7';

const ASSETS_TO_CACHE = [
    '/offline.html',
    '/app.css',
    '/css/mobile-overrides.css',
    '/css/face-capture.css',
    '/css/animations.css',
    '/lib/bootstrap/dist/css/bootstrap.min.css',
    '/js/analytics.js',
    '/favicon.png',
    '/manifest.json'
];

function shouldHandleFetch(event) {
    if (event.request.method !== 'GET') return false;
    // Let the browser handle full document navigations (Blazor Server, admin pages, etc.)
    if (event.request.mode === 'navigate') return false;

    let url;
    try {
        url = new URL(event.request.url);
    } catch {
        return false;
    }

    if (url.origin !== self.location.origin) return false;

    const path = url.pathname;
    if (path.startsWith('/api/') ||
        path.startsWith('/admin') ||
        path.startsWith('/uploads') ||
        path.startsWith('/_framework') ||
        path.startsWith('/hubs/')) {
        return false;
    }
    if (url.href.includes('/_blazor') || url.href.includes('/signalr')) {
        return false;
    }

    const exact = ['/app.css', '/manifest.json', '/favicon.png', '/offline.html'];
    if (exact.includes(path)) return true;
    if (path.startsWith('/css/') || path.startsWith('/js/') || path.startsWith('/lib/')) return true;

    return false;
}

self.addEventListener('install', event => {
    console.log('[Service Worker] Installing...');
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => {
                console.log('[Service Worker] Caching app shell');
                return cache.addAll(ASSETS_TO_CACHE);
            })
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    console.log('[Service Worker] Activating...');
    event.waitUntil(
        caches.keys().then(keyList => {
            return Promise.all(keyList.map(key => {
                if (key !== CACHE_NAME) {
                    console.log('[Service Worker] Removing old cache', key);
                    return caches.delete(key);
                }
            }));
        })
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    if (!shouldHandleFetch(event)) return;

    // Prefer the network so a previous deployment cannot keep old UI scripts and CSS alive.
    // Serve cached static assets only when offline.
    event.respondWith(
        fetch(event.request).then(networkResponse => {
            if (networkResponse.ok) {
                const copy = networkResponse.clone();
                event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.put(event.request, copy)));
            }
            return networkResponse;
        }).catch(async () => {
            const cached = await caches.match(event.request);
            return cached || new Response('Offline', { status: 503, statusText: 'Service Unavailable' });
        })
    );
});
