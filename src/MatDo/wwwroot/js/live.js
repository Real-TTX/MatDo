/* MatDo: the open window stays up to date (GET /api/app/events, Server-Sent Events, LiveService). A task that comes in by the webhook (dictated with a
   shortcut) or a change made on another device makes the lists of this window reload at once, but never while someone is typing, has a dialog or a
   panel open or drags a row: then it waits until that is done. After the reload the new task is marked for a moment and, from the webhook, named in a note.
   The stream is open only while the window is visible; a window that was in the background for a while looks again when it comes back. */
(function () {
  'use strict';

  if (!window.EventSource || !document.documentElement.getAttribute('data-user')) { return; }
  // only pages that show tasks
  if (!document.querySelector('.md-tasks, .md-view')) { return; }

  var TEXTS = (function () { try { return JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { return {}; } })();
  function t(key, a) { return (TEXTS[key] || key).replace('{0}', a === undefined ? '' : a); }
  var NOTE = 'md-live-note';

  // ---- after a reload: mark the task and say where it came from -----------------------------------------------------------
  (function () {
    var note = null;
    try { note = JSON.parse(sessionStorage.getItem(NOTE) || 'null'); sessionStorage.removeItem(NOTE); } catch (e) { /* private mode */ }
    if (!note) { return; }
    var row = note.task ? document.querySelector('[data-task="' + note.task + '"]') : null;
    if (row) {
      row.classList.add('md-task--new');
      setTimeout(function () { row.classList.remove('md-task--new'); }, 2600);
    }
    if (note.source === 'webhook' && note.title && window.MatUi && MatUi.toast) {
      setTimeout(function () { MatUi.toast(t('Js.Live.Webhook', note.title), { tone: 'info', timeout: 4000 }); }, 50);
    }
  })();

  // ---- reload when nothing is in the way --------------------------------------------------------------------------------------
  var pending = null;
  function busy() {
    var a = document.activeElement;
    if (a && (a.matches('input:not([type=checkbox]):not([type=radio]), textarea, select, [contenteditable]') )) { return true; }
    if (document.querySelector('dialog[open], .mat-itemlist.is-reordering, .md-task.is-leaving, .mat-item.is-swiping')) { return true; }
    try { if (document.querySelector(':popover-open')) { return true; } } catch (e) { /* no popovers */ }
    var typed = Array.prototype.some.call(document.querySelectorAll('[data-qa-input]'), function (i) { return i.value.trim() !== ''; });
    return typed;
  }
  function reload() {
    if (!pending) { return; }
    if (busy()) { setTimeout(reload, 1000); return; }
    try { sessionStorage.setItem(NOTE, JSON.stringify(pending)); } catch (e) { /* private mode */ }
    pending = null;
    location.reload();
  }

  // ---- the stream ---------------------------------------------------------------------------------------------------------------
  var source = null;
  var hiddenAt = 0;
  function open() {
    if (source) { return; }
    source = new EventSource('/api/app/events?tab=' + encodeURIComponent(window.MatDoTab || ''));
    source.addEventListener('tasks', function (e) {
      var data = {};
      try { data = JSON.parse(e.data); } catch (x) { /* an empty note */ }
      var first = !pending;
      pending = { task: data.task || null, title: data.title || '', source: data.source || '' };
      if (first) { setTimeout(reload, 150); } // a few changes in a row are one reload
    });
  }
  function close() { if (source) { source.close(); source = null; } }

  document.addEventListener('visibilitychange', function () {
    if (document.hidden) { hiddenAt = Date.now(); close(); return; }
    open();
    // what came while the window was away for a while: look again (the page of the window is older than the news)
    if (hiddenAt && Date.now() - hiddenAt > 30000 && !pending) { pending = { task: null, title: '', source: '' }; reload(); }
    hiddenAt = 0;
  });
  window.addEventListener('pagehide', close);
  if (!document.hidden) { open(); }
})();
