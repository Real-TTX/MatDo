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

// ---- notifications (Web Push: NotificationService on the server, push.js subscribes) -------------------------------------
// A message is JSON: title, body, url (opened by a click), tag (a new one of the same task replaces the old), task, user, badge (the number on the app
// icon: open tasks of today and overdue), actions ([{action, title}]: "complete" and "tomorrow" are done here, without opening the app).
function setBadge(count) {
  if (!('setAppBadge' in self.navigator)) { return Promise.resolve(); }
  return (count ? self.navigator.setAppBadge(count) : self.navigator.clearAppBadge()).catch(() => {});
}

self.addEventListener('push', event => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch (e) { data = { title: 'MatDo', body: event.data ? event.data.text() : '' }; }
  const options = {
    body: data.body || '',
    icon: '/img/icon-192.png',
    badge: '/img/badge-96.png',
    tag: data.tag || undefined,
    renotify: !!data.tag,
    timestamp: Date.now(),
    data: { url: data.url || '/Today', task: data.task || null, user: data.user || null },
    actions: (data.actions || []).slice(0, 2),
  };
  event.waitUntil(Promise.all([
    self.registration.showNotification(data.title || 'MatDo', options),
    typeof data.badge === 'number' ? setBadge(data.badge) : Promise.resolve(),
  ]));
});

// tomorrow, in the calendar of the device: the person's day
function tomorrowIso() {
  const d = new Date(); d.setDate(d.getDate() + 1);
  return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
}

// an action of a notification: sent now; without a network it waits in the queue of the offline mode like any change
async function act(url, body, user) {
  const id = (self.crypto && self.crypto.randomUUID) ? self.crypto.randomUUID() : String(Date.now()) + Math.random();
  try {
    const response = await fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json', 'Content-Type': 'application/json', 'X-MatDo-Request': '1', 'X-MatDo-User': user || '', 'X-MatDo-Request-Id': id },
      body: JSON.stringify(body),
    });
    if (response.ok) { return; }
    if (response.status !== 401) { return; } // gone, or another account: nothing to wait for
  } catch (e) { /* no network: queue it */ }
  const db = await openDb();
  await run(db, 'readwrite', s => s.add({ url, body, user: user || '', id, at: Date.now() }));
  if (self.registration.sync) { await self.registration.sync.register('md-outbox').catch(() => {}); }
}

self.addEventListener('notificationclick', event => {
  const n = event.notification;
  const data = n.data || {};
  n.close();
  if (data.task && (event.action === 'complete' || event.action === 'tomorrow')) {
    const url = '/api/app/tasks/' + data.task + (event.action === 'complete' ? '/complete' : '/due');
    const body = event.action === 'complete' ? {} : { date: tomorrowIso() };
    event.waitUntil(act(url, body, data.user ? String(data.user) : ''));
    return;
  }
  // open the page: in a window of the app that is open already, else in a new one
  const target = new URL(data.url || '/Today', self.location.origin).href;
  event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(windows => {
    const open = windows.find(w => new URL(w.url).origin === self.location.origin);
    if (open) { return open.focus().then(w => (w || open).navigate ? (w || open).navigate(target) : null).catch(() => self.clients.openWindow(target)); }
    return self.clients.openWindow(target);
  }));
});

// the browser renewed the subscription by itself: tell the server the new one (the page sends it the next time otherwise)
self.addEventListener('pushsubscriptionchange', event => {
  const next = event.newSubscription;
  if (!next) { return; }
  const json = next.toJSON();
  event.waitUntil(fetch('/api/app/push/subscribe', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'X-MatDo-Request': '1' },
    body: JSON.stringify({ endpoint: json.endpoint, p256dh: json.keys && json.keys.p256dh, auth: json.keys && json.keys.auth }),
  }).catch(() => {}));
});
