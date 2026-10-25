/* MatDo: the quick add editor (Pages/Shared/_QuickAdd.cshtml), like Todoist's.

   While the title is typed, the server reads it (GET /api/app/parse, the same parser that creates the task): the parts it understood are marked in the text
   (a mirror behind the transparent textarea) and shown as chips under it. The x of a chip stops reading that kind (a date that is really part of the title).
   Date, priority and labels can also be picked with the buttons; what was picked by hand wins over the text. Enter adds the task, Shift+Enter is a new line,
   Escape closes the editor. Q (outside a field) opens the dialog of the quick add. Without this script the form still works: the server reads the text. */
(function () {
  'use strict';

  // the texts of the page in its language (ClientTexts: every key that starts with "Js.")
  var TEXTS = (function () { try { return JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { return {}; } })();
  function t(key, a) { return (TEXTS[key] || key).replace('{0}', a === undefined ? '' : a); }

  function $(sel, scope) { return (scope || document).querySelector(sel); }
  function $$(sel, scope) { return Array.prototype.slice.call((scope || document).querySelectorAll(sel)); }
  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function icon(name) { return '<svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#mat-i-' + name + '"></use></svg>'; }
  // a date picked by hand, as the server writes it (the shared label of pick.js)
  function label(isoDate, time) { return MatDoPick.label(isoDate, time); }

  function init(root) {
    if (root.__qa) { return; }
    root.__qa = true;
    var form = $('[data-qa-form]', root);
    var input = $('[data-qa-input]', root);
    var mirror = $('[data-qa-mirror]', root);
    var desc = $('[data-qa-desc]', root);
    var chips = $('[data-qa-chips]', root);
    var off = $('[data-qa-off]', root);
    var due = $('[data-qa-due]', root);
    var time = $('[data-qa-time]', root);
    var noDate = $('[data-qa-nodate]', root);
    var prio = $('[data-qa-priority]', root);
    var labels = $('[data-qa-labels]', root);
    var touched = $('[data-qa-touched]', root);
    var project = $('[data-qa-project]', root);
    var defaultDue = $('[data-qa-default-due]', root);
    var disabled = {};
    var tokens = [];
    var parsed = null;
    var timer = 0, seq = 0;

    function autosize(el) { el.style.height = 'auto'; el.style.height = el.scrollHeight + 'px'; }

    // the marks: tokens whose text is still where it was stay while the next answer is on its way
    function paint() {
      var text = input.value;
      var html = '', at = 0;
      tokens.filter(function (t) { return text.substr(t.start, t.length) === t.text; })
        .sort(function (a, b) { return a.start - b.start; })
        .forEach(function (t) {
          if (t.start < at) { return; }
          html += esc(text.slice(at, t.start)) + '<mark class="md-hl--' + t.kind + '">' + esc(text.substr(t.start, t.length)) + '</mark>';
          at = t.start + t.length;
        });
      mirror.innerHTML = html + esc(text.slice(at)) + '​';
    }

    function chip(kind, iconName, text, extra, manual) {
      return '<span class="md-chip' + (manual ? ' md-chip--manual' : '') + '" data-chip="' + kind + '">' + icon(iconName) + '<span>' + esc(text) + '</span>' + (extra || '') +
        '<button type="button" class="md-chip__x" data-chip-x="' + kind + '" aria-label="' + esc(t('Js.Chip.Remove').replace('{0}', t('Js.Kind.' + kind.split(':')[0]))) + '">' + icon('x') + '</button></span>';
    }

    function renderChips() {
      var p = parsed || {};
      var html = '';
      // the chips are what the text says; a date or a priority picked by hand shows on its button only (like Todoist)
      if (noDate.value !== 'true' && !due.value && p.dueText) {
        html += chip('date', p.recurrence ? 'repeat' : 'calendar', p.dueText + (p.recurrence ? ' · ' + p.recurrence : ''));
      }
      if (p.project && touched.value !== 'true') { html += chip('project', 'hash', p.project.name, p.project.isNew ? '<span class="md-chip__new">' + esc(t('Js.New')) + '</span>' : ''); }
      if (p.section) { html += chip('section', 'list', p.section); }
      if (!prio.value && p.priority) { html += chip('priority', 'flag', 'P' + p.priority); }
      (p.labels || []).forEach(function (l) { html += chip('label', 'tag', l); });
      if (labels.value) { labels.value.split(',').forEach(function (l) { if (l) { html += chip('manuallabel:' + l, 'tag', l, '', true); } }); }
      if (p.deadlineText) { html += chip('deadline', 'target', t('Js.Deadline').replace('{0}', p.deadlineText)); }
      if (p.description) { html += chip('description', 'file-text', t('Js.Kind.description')); }
      chips.innerHTML = html;
      var dateLabel = $('[data-qa-date-label]', root), prioLabel = $('[data-qa-priority-label]', root);
      var fallback = defaultDue && defaultDue.value && !p.dueText ? label(defaultDue.value) : '';
      dateLabel.textContent = noDate.value === 'true' ? t('Js.NoDate') : due.value ? label(due.value, time.value) : fallback || t('Js.Kind.date');
      prioLabel.textContent = prio.value ? 'P' + prio.value : t('Js.Kind.priority');
      // a button with a value picked by hand shows it, also on a phone (where the others show their icon only)
      dateLabel.parentNode.classList.toggle('is-set', noDate.value === 'true' || !!due.value || !!fallback);
      prioLabel.parentNode.classList.toggle('is-set', !!prio.value);
    }

    function parse() {
      var id = ++seq;
      var text = input.value;
      if (!text.trim()) { tokens = []; parsed = null; paint(); renderChips(); return; }
      var url = '/api/app/parse?text=' + encodeURIComponent(text) + '&off=' + encodeURIComponent(off.value);
      fetch(url, { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (data) {
          if (!data || id !== seq) { return; }
          parsed = data;
          tokens = data.tokens.map(function (t) { return { kind: t.kind, start: t.start, length: t.length, text: text.substr(t.start, t.length) }; });
          paint();
          renderChips();
        })
        .catch(function () { /* offline: the server reads the text when the form is sent */ });
    }

    function schedule() { clearTimeout(timer); timer = setTimeout(parse, 140); }

    function setOff(kind, on) {
      if (on) { disabled[kind] = true; } else { delete disabled[kind]; }
      off.value = Object.keys(disabled).join(',');
    }

    // the panels (date, priority, labels) are the shared ones of pick.js (popovers above the dialog, a sheet on a phone)
    function closePops() { MatDoPick.closeAll(); }

    function reset() {
      form.reset();
      due.value = time.value = prio.value = off.value = '';
      labels.value = labels.defaultValue;
      noDate.value = 'false';
      touched.value = 'false';
      disabled = {};
      tokens = []; parsed = null;
      $('[data-qa-label]', root).forEach(function (c) { c.checked = c.defaultChecked; });
      // the pickers (project, time) show the values the reset put back
      if (window.MatUi && MatUi.picker) { MatUi.picker.init(root); }
      paint(); renderChips(); autosize(input); autosize(desc);
    }

    input.addEventListener('input', function () { autosize(input); paint(); schedule(); });
    input.addEventListener('scroll', function () { mirror.scrollTop = input.scrollTop; });
    desc.addEventListener('input', function () { autosize(desc); });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
        e.preventDefault();
        if (input.value.trim()) { form.requestSubmit ? form.requestSubmit() : form.submit(); }
      } else if (e.key === 'Escape') {
        e.preventDefault();
        cancel();
      }
    });
    desc.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); if (input.value.trim()) { form.requestSubmit(); } }
      if (e.key === 'Escape') { e.preventDefault(); cancel(); }
    });

    function cancel() {
      reset();
      closePops();
      var dialog = root.closest('dialog');
      if (dialog) { dialog.close(); } else { root.open = false; }
    }

    $('[data-qa-cancel]', root).addEventListener('click', cancel);
    if (project) { project.addEventListener('change', function () { touched.value = 'true'; renderChips(); }); }

    root.addEventListener('toggle', function () { if (root.open) { setTimeout(function () { input.focus(); autosize(input); }, 0); } });

    chips.addEventListener('click', function (e) {
      var x = e.target.closest('[data-chip-x]');
      if (!x) { return; }
      var kind = x.getAttribute('data-chip-x');
      if (kind === 'manualdate') { due.value = time.value = ''; noDate.value = 'false'; setOff('date', false); }
      else if (kind === 'manualpriority') { prio.value = ''; setOff('priority', false); }
      else if (kind.indexOf('manuallabel:') === 0) {
        var name = kind.slice(12);
        labels.value = labels.value.split(',').filter(function (l) { return l && l !== name; }).join(',');
        $$('[data-qa-label]', root).forEach(function (c) { if (c.value === name) { c.checked = false; } });
      } else { setOff(kind, true); }
      parse();
      input.focus();
    });

    // the date: the shared panel (pick.js); what is picked by hand wins over the text
    function pickDate(date, t) {
      due.value = date || '';
      time.value = date ? (t || '') : '';
      noDate.value = date ? 'false' : 'true';
      setOff('date', true);
      parse(); input.focus();
    }
    var datePop = $('[data-pick-pop="date"]', root);
    MatDoPick.due(datePop, {
      get: function () { return { date: due.value, time: time.value }; },
      pick: function (s) { pickDate(s.date, s.time); },
      // a time without a day: today (like the text "at 3pm"); with a day: that day at this time
      time: function (value) {
        if (!value) { time.value = ''; renderChips(); return; }
        if (!due.value) { due.value = MatDoPick.iso(new Date()); noDate.value = 'false'; setOff('date', true); }
        time.value = value;
        parse();
      },
    });
    MatDoPick.priority($('[data-pick-pop="priority"]', root), {
      get: function () { return prio.value; },
      pick: function (value) { prio.value = value; setOff('priority', true); parse(); input.focus(); },
    });
    $$('[data-qa-label]', root).forEach(function (c) {
      c.addEventListener('change', function () {
        labels.value = $$('[data-qa-label]', root).filter(function (x) { return x.checked; }).map(function (x) { return x.value; }).join(',');
        renderChips();
      });
    });

    // The task is sent as JSON (the page then shows it: a reload with the editor open again). Without a network it is noted on the device instead (offline.js)
    // and goes into the inbox when the network is back, read like the webhook. Without the script: the form.
    function sendOrNote(body, title) {
      if (!navigator.onLine) { return Promise.reject({ offline: true }); }
      return fetch('/api/app/tasks', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Accept': 'application/json', 'Content-Type': 'application/json', 'X-MatDo-Request': '1', 'X-MatDo-Tab': window.MatDoTab || '' },
        body: JSON.stringify(body),
      }).then(function (r) {
        return r.json().catch(function () { return {}; }).then(function (data) { if (!r.ok) { throw { data: data }; } return data; });
      }, function () { throw { offline: true }; });
    }
    form.addEventListener('submit', function (e) {
      if (!input.value.trim()) { e.preventDefault(); input.focus(); return; }
      clearTimeout(timer);
      if (!window.fetch) { return; }
      e.preventDefault();
      var fields = new FormData(form);
      var body = {};
      ['Text', 'Description', 'DefaultProjectId', 'DefaultSectionId', 'DefaultDueDate', 'ParentId', 'Off', 'DueDate', 'DueTime', 'Priority', 'Labels'].forEach(function (k) {
        var v = fields.get(k);
        body[k.charAt(0).toLowerCase() + k.slice(1)] = v === null || v === '' ? null : String(v);
      });
      ['defaultProjectId', 'defaultSectionId', 'parentId'].forEach(function (k) { body[k] = body[k] ? Number(body[k]) : null; });
      body.noDate = fields.get('NoDate') === 'true';
      body.projectTouched = fields.get('ProjectTouched') === 'true';
      var title = (parsed && parsed.title) || input.value.trim();
      var submit = form.querySelector('[data-qa-submit]');
      submit.disabled = true;
      sendOrNote(body, title).then(function (data) {
        submit.disabled = false;
        try { sessionStorage.setItem('md-toast', data && data.message ? data.message : title); } catch (x) { /* private mode */ }
        var dialog = root.closest('dialog');
        var url = new URL(location.href);
        if (!dialog) { url.searchParams.set('qa', root.id); url.hash = root.id; }
        location.replace(url.toString());
        if (url.pathname === location.pathname && url.search === location.search) { location.reload(); }
      }, function (err) {
        submit.disabled = false;
        if (err && err.offline && window.MatDoOffline) {
          // the text as typed: the server reads it later like the webhook (date, #project, keywords)
          window.MatDoOffline.note(input.value);
          reset();
          if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(t('Js.Offline.Noted', title), { tone: 'warning', timeout: 5000 }); }
          input.focus();
          return;
        }
        var message = err && err.data && err.data.error ? err.data.error : t('Js.Task.Failed');
        if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: 'danger' }); }
      });
    });

    paint();
    renderChips(); // the buttons show what the editor starts with (the day of the page, its labels)
    if (root.open) { autosize(input); }
  }

  $$('[data-quickadd]').forEach(init);

  // ?qa=<id> after an added task: that editor is open again and has the focus (the server opened it)
  var again = new URLSearchParams(location.search).get('qa');
  if (again) {
    var el = document.getElementById(again);
    if (el && el.open) { var i = $('[data-qa-input]', el); if (i) { i.focus(); } }
    var url = new URL(location.href);
    url.searchParams.delete('qa');
    history.replaceState(null, '', url.pathname + url.search);
  }

  // Q: the quick add dialog (not while typing somewhere)
  var dialog = document.getElementById('md-quick');
  function openQuick() {
    if (!dialog) { return; }
    if (!dialog.open) { dialog.showModal(); }
    var i = $('[data-qa-input]', dialog);
    setTimeout(function () { i.focus(); }, 0);
  }
  document.addEventListener('keydown', function (e) {
    if ((e.key === 'q' || e.key === 'Q') && !e.ctrlKey && !e.metaKey && !e.altKey) {
      var t = e.target;
      if (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName) || document.querySelector('dialog[open]')) { return; }
      e.preventDefault();
      openQuick();
    }
  });
  if (dialog) {
    dialog.addEventListener('close', function () { var f = $('[data-qa-form]', dialog); if (f) { f.reset(); } });
    $$('[data-dialog-open="md-quick"]').forEach(function (b) { b.addEventListener('click', function () { setTimeout(function () { $('[data-qa-input]', dialog).focus(); }, 30); }); });
    $$('[data-quick-open]').forEach(function (a) { a.addEventListener('click', function (e) { e.preventDefault(); openQuick(); }); });
  }
})();
