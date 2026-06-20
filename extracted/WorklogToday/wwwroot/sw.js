// worklog.today service worker — app-shell cache + offline fallback.
const CACHE = 'worklog-v1';
const SHELL = [
  '/css/site.css',
  '/js/app.js',
  '/favicon.svg',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/manifest.webmanifest'
];

self.addEventListener('install', e => {
  e.waitUntil(caches.open(CACHE).then(c => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', e => {
  e.waitUntil(
    caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', e => {
  const req = e.request;
  if (req.method !== 'GET') return; // never cache mutations / API writes

  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;

  // Network-first for navigations & API, fall back to cache when offline.
  if (req.mode === 'navigate' || url.pathname.startsWith('/api/')) {
    e.respondWith(
      fetch(req).catch(() => caches.match(req).then(r => r || caches.match('/app')))
    );
    return;
  }

  // Cache-first for static shell assets.
  e.respondWith(
    caches.match(req).then(cached => cached || fetch(req).then(resp => {
      const copy = resp.clone();
      caches.open(CACHE).then(c => c.put(req, copy)).catch(() => {});
      return resp;
    }).catch(() => cached))
  );
});
