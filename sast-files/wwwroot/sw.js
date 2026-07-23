/* Kumburgaz PWA service worker
   Statik varlik onbellegi + cevrimdisi sayfasi + Web Push (Asama 5).
   Kural: HTML/auth'lu yanitlar ASLA onbellege yazilmaz. */

const STATIC_CACHE = 'kumburgaz-static-v2';
const OFFLINE_URL = '/offline.html';

const PRECACHE = [
    OFFLINE_URL,
    '/css/mobile.css',
    '/lib/bootstrap/dist/css/bootstrap.min.css',
    '/img/icons/icon-192.png',
    '/manifest.webmanifest'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(STATIC_CACHE)
            .then((cache) => cache.addAll(PRECACHE))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(
                keys.filter((k) => k !== STATIC_CACHE).map((k) => caches.delete(k))
            ))
            .then(() => self.clients.claim())
    );
});

function isStaticAsset(url) {
    return /^\/(lib|css|js|img)\//.test(url.pathname) || url.pathname === '/manifest.webmanifest';
}

self.addEventListener('fetch', (event) => {
    const req = event.request;
    const url = new URL(req.url);

    // Yalnizca GET ve ayni origin; digerlerine (POST, /Identity, harici) dokunma.
    if (req.method !== 'GET' || url.origin !== self.location.origin) {
        return;
    }
    if (url.pathname.startsWith('/Identity')) {
        return;
    }

    // Statik varliklar: cache-first.
    if (isStaticAsset(url)) {
        event.respondWith(
            caches.match(req).then((cached) => cached || fetch(req).then((res) => {
                if (res.ok) {
                    const copy = res.clone();
                    caches.open(STATIC_CACHE).then((cache) => cache.put(req, copy));
                }
                return res;
            }).catch(() => cached))
        );
        return;
    }

    // Sayfa (HTML) istekleri: network-first, basarisizsa cevrimdisi sayfasi. Yanit onbellege YAZILMAZ.
    if (req.mode === 'navigate' || (req.headers.get('accept') || '').includes('text/html')) {
        event.respondWith(
            fetch(req).catch(() => caches.match(OFFLINE_URL))
        );
        return;
    }

    // Diger GET: dogrudan agdan.
});

self.addEventListener('push', (event) => {
    let data = {};
    try {
        data = event.data ? event.data.json() : {};
    } catch (e) {
        data = {};
    }

    const title = data.title || 'Kumburgaz';
    const options = {
        body: data.body || '',
        icon: '/img/icons/icon-192.png',
        badge: '/img/icons/icon-192.png',
        data: { url: data.url || '/m' }
    };

    event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', (event) => {
    event.notification.close();
    const url = (event.notification.data && event.notification.data.url) || '/m';

    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
            for (const client of clientList) {
                if (client.url.includes(url) && 'focus' in client) {
                    return client.focus();
                }
            }
            if (clients.openWindow) {
                return clients.openWindow(url);
            }
        })
    );
});
