/* MatDo service worker: the app as a PWA, also without a network.
   - Static files (toolkit, styles, scripts, icons): from the cache first, refreshed in the background (their addresses carry ?v=<hash>).
   - Pages: from the network; each page that was seen is kept (cache "matdo-pages"), so without a network the last copy is shown (the page says "offline"),
     else the offline page. The copies are deleted at sign-out (message "clear-pages" from offline.js) and whenever the sign-in page is opened.
   - Changes made without a network wait in IndexedDB (offline.js); "sync" (Background Sync, Android) sends them while the app is closed.
   - POST requests and the APIs are never cached. */
const VERSION = 'matdo-v6';
const PAGES = 'matdo-pages';
const SHELL = ['/offline.html', '/matui/matui.css', '/css/app.css', '/favicon.svg', '/img/icon-192.png', '/img/icon-512.png', '/img/logo/matdo-logo.svg', '/img/logo/matdo-logo-dark.svg', '/manifest.webmanifest'];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(VERSION).then(cache => cache.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil(caches.keys()
    .then(keys => Promise.all(keys.filter(k => k !== VERSION && k !== PAGES).map(k => caches.delete(k))))
    .then(() => self.clients.claim()));
});

self.addEventListener('message', event => {
  if (event.data === 'clear-pages') { event.waitUntil(caches.delete(PAGES)); }
});

function isStatic(url) {
  return /^\/(matui|css|js|img)\//.test(url.pathname) || url.pathname === '/favicon.svg' || url.pathname === '/manifest.webmanifest';
}

// pages that belong to no one are not kept
function keepPage(url) {
  return !url.pathname.startsWith('/Account/Login') && !url.pathname.startsWith('/Account/Register') && !url.pathname.startsWith('/Account/Logout') && url.pathname !== '/Error';
}

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') { return; }
  const url = new URL(request.url);
  if (url.origin !== self.location.origin || url.pathname.startsWith('/api/') || url.pathname === '/health' || url.pathname === '/sw.js') { return; }

  if (isStatic(url)) {
    event.respondWith(caches.open(VERSION).then(cache => cache.match(request).then(hit => {
      const fresh = fetch(request).then(response => { if (response.ok) { cache.put(request, response.clone()); } return response; }).catch(() => hit);
      return hit || fresh;
    })));
    return;
  }

  if (request.mode === 'navigate') {
    if (url.pathname.startsWith('/Account/Login')) { event.waitUntil(caches.delete(PAGES)); }
    event.respondWith(fetch(request).then(response => {
      if (response.ok && !response.redirected && response.type === 'basic' && keepPage(url)) {
        const copy = response.clone();
        event.waitUntil(caches.open(PAGES).then(cache => cache.put(url.pathname + url.search, copy)));
      }
      return response;
    }).catch(() => caches.open(PAGES)
      .then(cache => cache.match(url.pathname + url.search).then(hit => hit || cache.match(url.pathname, { ignoreSearch: true })))
      .then(hit => hit || caches.match('/offline.html'))));
  }
});

// ---- the queue of changes made without a network (the same store as offline.js) ---------------------------------------
function openDb() {
  return new Promise((resolve, reject) => {
    const open = indexedDB.open('matdo', 1);
    open.onupgradeneeded = () => open.result.createObjectStore('outbox', { keyPath: 'key', autoIncrement: true });
    open.onsuccess = () => resolve(open.result);
    open.onerror = () => reject(open.error);
  });
}

function run(db, mode, work) {
  return new Promise((resolve, reject) => {
    const store = db.transaction('outbox', mode).objectStore('outbox');
    const req = work(store);
    store.transaction.oncomplete = () => resolve(req ? req.result : undefined);
    store.transaction.onerror = () => reject(store.transaction.error);
  });
}

async function flushOutbox() {
  const db = await openDb();
  const entries = await run(db, 'readonly', s => s.getAll());
  let sent = 0;
  for (const entry of entries) {
    // a network error rejects: the browser tries the sync again later
    const response = await fetch(entry.url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json', 'Content-Type': 'application/json', 'X-MatDo-Request': '1', 'X-MatDo-User': entry.user || '', 'X-MatDo-Request-Id': entry.id || '' },
      body: JSON.stringify(entry.body || {}),
    });
    if (response.status === 401) { break; } // signed out: the page sends it after the next sign-in of the same account
    await run(db, 'readwrite', s => s.delete(entry.key));
    if (response.ok) { sent++; }
  }

  const windows = await self.clients.matchAll({ type: 'window' });
  windows.forEach(w => w.postMessage({ type: 'outbox-synced', count: sent }));
}

self.addEventListener('sync', event => {
  if (event.tag === 'md-outbox') { event.waitUntil(flushOutbox()); }
});
