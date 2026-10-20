/* MatDo without a network – small on purpose:
   - The pages need the network. Without one the service worker shows the offline page (offline.html); an open page shows "Offline" in the top bar.
   - New tasks can still be noted: on the offline page, or in the quick add while offline. The notes stay on this device (localStorage md-notes) and go into the
     inbox when the network is back: POST /api/app/capture, read the same way as the webhook (keywords, dates, #project). Nothing else waits: checking off,
     a date, delete need the network.
   - window.MatDoTab: this window (live.js; the server does not tell the window that made a change). */
(function () {
  'use strict';

  var NOTES = 'md-notes', USER = 'md-user', LANG = 'md-lang';
  var root = document.documentElement;
  var user = root.getAttribute('data-user') || '';
  var TEXTS = (function () { try { return JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { return {}; } })();
  function t(key, a) { return (TEXTS[key] || key).replace('{0}', a === undefined ? '' : a); }
  function newId() { return (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : String(Date.now()) + String(Math.random()).slice(2); }
  function store(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* private mode */ } }
  function read(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }

  // this window, kept over reloads
  var tab = (function () {
    try { var v = sessionStorage.getItem('md-tab'); if (!v) { v = newId(); sessionStorage.setItem('md-tab', v); } return v; } catch (e) { return newId(); }
  })();
  window.MatDoTab = tab;

  // the offline page does not know who uses the device: an app page writes it down (a note belongs to that account)
  if (user) { store(USER, user); store(LANG, root.lang || 'en'); }
  var owner = user || read(USER) || '';

  // ---- the notes --------------------------------------------------------------------------------------------------------------
  function notes() { try { return JSON.parse(read(NOTES) || '[]'); } catch (e) { return []; } }
  function mine() { return notes().filter(function (n) { return !n.user || !owner || n.user === owner; }); }
  function save(list) { store(NOTES, JSON.stringify(list)); render(); badge(); }
  function note(text) {
    text = String(text || '').trim();
    if (!text) { return false; }
    var list = notes();
    list.push({ id: newId(), text: text.slice(0, 2000), user: owner, at: Date.now() });
    save(list);
    return true;
  }
  function drop(id) { save(notes().filter(function (n) { return n.id !== id; })); }

  // one after the other into the inbox; the id makes a note that is sent twice one task
  var sending = false;
  function flush() {
    var todo = mine();
    if (!user || sending || !navigator.onLine || !todo.length) { return; }
    sending = true;
    var sent = 0;
    todo.reduce(function (chain, n) {
      return chain.then(function () {
        return fetch('/api/app/capture', {
          method: 'POST', credentials: 'same-origin',
          headers: { 'Accept': 'application/json', 'Content-Type': 'application/json', 'X-MatDo-Request': '1', 'X-MatDo-User': n.user || user, 'X-MatDo-Request-Id': n.id, 'X-MatDo-Tab': tab },
          body: JSON.stringify({ text: n.text }),
        }).then(function (r) {
          if (r.ok) { sent++; drop(n.id); return; }
          if (r.status === 400 || r.status === 409) { drop(n.id); return; } // nothing to read, or another account: it never goes
          throw new Error(String(r.status));
        });
      });
    }, Promise.resolve()).catch(function () { /* the network went again: the rest waits */ }).then(function () {
      sending = false;
      if (!sent) { return; }
      var message = t('Js.Offline.Sent', sent);
      if (document.querySelector('.md-tasks, .md-view')) {
        try { sessionStorage.setItem('md-toast', message); } catch (e) { /* private mode */ }
        location.reload();
      } else if (window.MatUi && MatUi.toast) { MatUi.toast(message, { tone: 'success' }); }
    });
  }

  // ---- the top bar of an app page: "Offline" and how many notes wait; a click opens the offline page (or sends them) ---------
  var mark = document.getElementById('md-offline');
  function badge() {
    if (!mark) { return; }
    var n = mine().length, off = !navigator.onLine;
    mark.hidden = !off && !n;
    mark.querySelector('[data-offline-text]').textContent = off ? t('Js.Offline.Badge') + (n ? ' · ' + t('Js.Offline.BadgeNotes', n) : '') : t('Js.Offline.BadgeNotes', n);
    mark.title = off ? t('Js.Offline.Now') : '';
  }
  if (mark) { mark.addEventListener('click', function () { if (navigator.onLine) { flush(); } else { location.assign('/offline.html'); } }); }

  // ---- the offline page: a field and the list of notes ------------------------------------------------------------------------
  var page = document.getElementById('md-capture');
  function render() {
    var list = document.getElementById('md-notes');
    if (!list) { return; }
    list.textContent = '';
    mine().forEach(function (n) {
      var li = document.createElement('li');
      li.className = 'md-note';
      var text = document.createElement('span');
      text.textContent = n.text;
      var x = document.createElement('button');
      x.type = 'button';
      x.className = 'mat-btn mat-btn--ghost mat-btn--icon mat-btn--sm';
      x.setAttribute('aria-label', list.getAttribute('data-remove-' + root.lang) || 'Remove');
      x.textContent = '×';
      x.addEventListener('click', function () { drop(n.id); });
      li.appendChild(text);
      li.appendChild(x);
      list.appendChild(li);
    });
  }
  if (page) {
    root.lang = read(LANG) || ((navigator.language || 'en').slice(0, 2) === 'de' ? 'de' : 'en');
    var field = document.getElementById('md-capture-text');
    field.placeholder = field.getAttribute('data-placeholder-' + root.lang) || '';
    page.addEventListener('submit', function (e) {
      e.preventDefault();
      if (note(field.value)) { field.value = ''; }
      field.focus();
    });
    render();
  }

  // back online: an app page sends the notes, the offline page goes back to the app (which sends them)
  window.addEventListener('online', function () { if (page) { location.assign('/Today'); return; } badge(); flush(); });
  window.addEventListener('offline', badge);
  window.MatDoOffline = { note: note, flush: flush };
  badge();
  flush();
})();
