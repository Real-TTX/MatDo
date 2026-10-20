/* MatDo service worker: the app as a PWA – small on purpose.
   - Static files (toolkit, styles, scripts, icons): from the cache first, refreshed in the background (their addresses carry ?v=<hash>).
   - Pages always come from the network; without one the offline page (offline.html), where new tasks can be noted (offline.js sends them later).
   - Notifications (Web Push): shown here, with the number on the app icon and the actions Done and Tomorrow.
   - POST requests and the APIs are never cached. */
const VERSION = 'matdo-v7';
const SHELL = ['/offline.html', '/js/offline.js', '/matui/matui.css', '/css/app.css', '/favicon.svg', '/img/icon-192.png', '/img/icon-512.png', '/img/badge-96.png', '/img/logo/matdo-logo.svg', '/img/logo/matdo-logo-dark.svg', '/manifest.webmanifest'];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(VERSION).then(cache => cache.addAll(SHELL)).then(() => self.skipWaiting()));
});

// an older version and the copies of pages an older version kept are gone
self.addEventListener('activate', event => {
  event.waitUntil(caches.keys()
    .then(keys => Promise.all(keys.filter(k => k !== VERSION).map(k => caches.delete(k))))
    .then(() => self.clients.claim()));
});

function isStatic(url) {
  return /^/(matui|css|js|img)//.test(url.pathname) || url.pathname === '/favicon.svg' || url.pathname === '/manifest.webmanifest' || url.pathname === '/offline.html';
}

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') { return; }
  const url = new URL(request.url);
  if (url.origin !== self.location.origin || url.pathname.startsWith('/api/') || url.pathname === '/health' || url.pathname === '/sw.js') { return; }

  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).catch(() => caches.match('/offline.html')));
    return;
  }

  if (isStatic(url)) {
    event.respondWith(caches.open(VERSION).then(cache => cache.match(request).then(hit => {
      const fresh = fetch(request).then(response => { if (response.ok) { cache.put(request, response.clone()); } return response; }).catch(() => hit);
      return hit || fresh;
    })));
  }
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

// an action of a notification (Done, Tomorrow): sent now; without a network it does not happen (the notification is gone, the task is unchanged)
function act(url, body, user) {
  return fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Accept': 'application/json', 'Content-Type': 'application/json', 'X-MatDo-Request': '1', 'X-MatDo-User': user || '' },
    body: JSON.stringify(body),
  }).catch(() => {});
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
