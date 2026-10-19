/* MatDo service worker: the app as a PWA.
   - The static files (toolkit, styles, scripts, icons) come from the cache first and are refreshed in the background (their addresses carry ?v=<hash>).
   - Pages always come from the network (they hold the tasks of a person: nothing of them is kept on the device); without a network the offline page is shown.
   - Requests that change something (POST) and the APIs are never cached. */
const VERSION = 'matdo-v4';
const SHELL = ['/offline.html', '/matui/matui.css', '/css/app.css', '/favicon.svg', '/img/icon-192.png', '/img/icon-512.png', '/img/logo/matdo-logo.svg', '/img/logo/matdo-logo-dark.svg', '/manifest.webmanifest'];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(VERSION).then(cache => cache.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil(caches.keys()
    .then(keys => Promise.all(keys.filter(k => k !== VERSION).map(k => caches.delete(k))))
    .then(() => self.clients.claim()));
});

function isStatic(url) {
  return /^\/(matui|css|js|img)\//.test(url.pathname) || url.pathname === '/favicon.svg' || url.pathname === '/manifest.webmanifest';
}

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') { return; }
  const url = new URL(request.url);
  if (url.origin !== self.location.origin || url.pathname.startsWith('/api/') || url.pathname === '/health') { return; }

  if (isStatic(url)) {
    // stale while revalidate
    event.respondWith(caches.open(VERSION).then(cache => cache.match(request).then(hit => {
      const fresh = fetch(request).then(response => { if (response.ok) { cache.put(request, response.clone()); } return response; }).catch(() => hit);
      return hit || fresh;
    })));
    return;
  }

  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).catch(() => caches.match('/offline.html')));
  }
});
