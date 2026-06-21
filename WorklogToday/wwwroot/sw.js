// worklog.today service worker — app-shell cache + offline fallback.
const CACHE = 'worklog-v5';
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
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', e => {
  const req = e.request;

  // Never intercept non-GET (POST/PUT/DELETE API calls go straight to network)
  if (req.method !== 'GET') return;

  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;

  // Always network-first for navigations and API calls.
  // This ensures the anti-forgery token in the page is always fresh,
  // and API responses are never stale.
  if (req.mode === 'navigate' || url.pathname.startsWith('/api/')) {
    e.respondWith(
      fetch(req).catch(() =>
        caches.match(req).then(r => r || caches.match('/app'))
      )
    );
    return;
  }

  // Cache-first for versioned static shell assets (css/js have asp-append-version).
  e.respondWith(
    caches.match(req).then(cached => {
      if (cached) return cached;
      return fetch(req).then(resp => {
        if (resp.ok) {
          const copy = resp.clone();
          caches.open(CACHE).then(c => c.put(req, copy)).catch(() => {});
        }
        return resp;
      });
    })
  );
});
