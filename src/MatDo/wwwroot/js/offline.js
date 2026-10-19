/* MatDo offline: the queue of changes made without a network (window.MatDoOffline).

   send(url, body)  posts JSON to the API (header X-MatDo-Request: 1, the account in X-MatDo-User). Without a network – or when the request fails on the way –
                    the change goes into the queue (IndexedDB "matdo", store "outbox") and the answer is { queued: true }: the page shows it at once.
   flush()          sends the queue in order: when the network comes back, when a page loads, every 30 s, and in the background (Background Sync, sw.js, Android).
                    An entry that the server refuses (4xx) is dropped with a message; one of another account (409) silently; a network error keeps it for later.
   clear()          empties the queue and the copies of the pages (sign-out: nothing of one person stays on the device for the next).

   The indicator in the top bar (#md-offline) says "Offline" and how many changes wait. */
(function () {
  'use strict';

  var TEXTS = (function () { try { return JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { return {}; } })();
  function t(key, a) { return (TEXTS[key] || key).replace('{0}', a === undefined ? '' : a); }
  function toast(message, tone) { if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success', timeout: 5000 }); } }

  var root = document.documentElement;
  var user = root.getAttribute('data-user') || '';
  var DB = 'matdo', STORE = 'outbox';

  function db() {
    return new Promise(function (resolve, reject) {
      if (!window.indexedDB) { reject(new Error('no indexedDB')); return; }
      var open = indexedDB.open(DB, 1);
      open.onupgradeneeded = function () { open.result.createObjectStore(STORE, { keyPath: 'key', autoIncrement: true }); };
      open.onsuccess = function () { resolve(open.result); };
      open.onerror = function () { reject(open.error); };
    });
  }
  function tx(mode, work) {
    return db().then(function (d) {
      return new Promise(function (resolve, reject) {
        var store = d.transaction(STORE, mode).objectStore(STORE);
        var result = work(store);
        store.transaction.oncomplete = function () { resolve(result && result.result !== undefined ? result.result : result); };
        store.transaction.onerror = function () { reject(store.transaction.error); };
      });
    });
  }
  function all() { return tx('readonly', function (s) { return s.getAll(); }); }
  function add(entry) { return tx('readwrite', function (s) { return s.add(entry); }); }
  function drop(key) { return tx('readwrite', function (s) { return s.delete(key); }); }

  function headers() {
    return { 'Accept': 'application/json', 'Content-Type': 'application/json', 'X-MatDo-Request': '1', 'X-MatDo-User': user };
  }

  function newId() { return (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : String(Date.now()) + String(Math.random()).slice(2); }

  // every change has an id from its first try on: the server does it once, however often it is sent (the page and the service worker may send the same queue)
  function request(url, body, account, id) {
    var h = headers();
    if (account) { h['X-MatDo-User'] = account; }
    if (id) { h['X-MatDo-Request-Id'] = id; }
    return fetch(url, { method: 'POST', credentials: 'same-origin', headers: h, body: JSON.stringify(body || {}) });
  }

  function enqueue(url, body, label, id) {
    return add({ id: id || newId(), url: url, body: body || {}, label: label || '', user: user, at: Date.now() })
      .then(function () {
        refresh();
        if ('serviceWorker' in navigator && window.SyncManager) {
          navigator.serviceWorker.ready.then(function (r) { return r.sync.register('md-outbox'); }).catch(function () { /* flushed by the page */ });
        }
        return { ok: true, queued: true, data: null };
      });
  }

  // send now, or keep it for later
  function send(url, body, label) {
    var id = newId();
    if (!navigator.onLine) { return enqueue(url, body, label, id); }
    return request(url, body, null, id).then(function (r) {
      return r.json().catch(function () { return {}; }).then(function (data) {
        if (!r.ok) { var e = new Error(r.status); e.status = r.status; e.data = data; throw e; }
        return { ok: true, queued: false, data: data };
      });
    }, function () { return enqueue(url, body, label, id); });
  }

  var flushing = null;
  function flush() {
    if (flushing || !navigator.onLine) { return flushing || Promise.resolve(0); }
    flushing = all().then(function (entries) {
      var sent = 0;
      return entries.reduce(function (chain, entry) {
        return chain.then(function (stop) {
          if (stop) { return true; }
          return request(entry.url, entry.body, entry.user, entry.id).then(function (r) {
            if (r.ok) { sent++; return drop(entry.key).then(function () { return false; }); }
            if (r.status === 409) { return drop(entry.key).then(function () { return false; }); }
            if (r.status === 401) { return true; } // signed out: try again after the sign-in (of the same account)
            return r.json().catch(function () { return {}; }).then(function (data) {
              toast(t('Js.Offline.Dropped', entry.label || (data && data.error) || r.status), 'danger');
              return drop(entry.key).then(function () { return false; });
            });
          }, function () { return true; }); // still no network
        });
      }, Promise.resolve(false)).then(function () { return sent; });
    }).catch(function () { return 0; }).then(function (sent) {
      flushing = null;
      refresh();
      if (sent > 0) { synced(sent); }
      return sent;
    });
    return flushing;
  }

  // after a sync the page shows the server's state again (unless someone is typing)
  function synced(count) {
    var message = t('Js.Offline.Synced', count);
    var typing = document.activeElement && /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement.tagName) && document.activeElement.value;
    if (typing) { toast(message); return; }
    try { sessionStorage.setItem('md-toast', message); } catch (e) { /* private mode */ }
    location.reload();
  }

  function clear() {
    var jobs = [tx('readwrite', function (s) { return s.clear(); }).catch(function () { })];
    if (window.caches) { jobs.push(caches.delete('matdo-pages')); }
    if (navigator.serviceWorker && navigator.serviceWorker.controller) { navigator.serviceWorker.controller.postMessage('clear-pages'); }
    return Promise.all(jobs);
  }

  // ---- the indicator in the top bar ------------------------------------------------------------------------------------
  var badge = document.getElementById('md-offline');
  function refresh() {
    var offline = !navigator.onLine;
    root.toggleAttribute('data-offline', offline);
    all().then(function (entries) { return entries.filter(function (e) { return e.user === user; }).length; }, function () { return 0; }).then(function (n) {
      if (!badge) { return; }
      badge.hidden = !offline && n === 0;
      badge.classList.toggle('is-offline', offline);
      var text = badge.querySelector('[data-offline-text]');
      text.textContent = offline ? (n > 0 ? t('Js.Offline.BadgePending', n) : t('Js.Offline.Badge')) : t('Js.Offline.Pending', n);
      badge.setAttribute('aria-label', text.textContent);
    });
  }

  window.addEventListener('online', function () { refresh(); flush(); });
  window.addEventListener('offline', function () { refresh(); toast(t('Js.Offline.Now'), 'warning'); });
  if (navigator.serviceWorker) {
    navigator.serviceWorker.addEventListener('message', function (e) { if (e.data && e.data.type === 'outbox-synced') { refresh(); if (e.data.count > 0) { synced(e.data.count); } } });
  }
  if (badge) { badge.addEventListener('click', function () { flush(); }); }
  setInterval(function () { if (navigator.onLine) { flush(); } }, 30000);

  // sign-out: send what waits (when there is a network), then nothing of this person stays on the device
  document.addEventListener('submit', function (e) {
    var form = e.target;
    if (!form.matches || !form.matches('form[action="/Account/Logout"]') || form.__cleared) { return; }
    e.preventDefault();
    flush().then(clear, clear).then(function () { form.__cleared = true; form.submit(); });
  });

  window.MatDoOffline = { send: send, flush: flush, clear: clear, refresh: refresh };
  refresh();
  if (navigator.onLine) { flush(); }
})();
