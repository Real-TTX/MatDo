/* MatDo: the quick add editor (Pages/Shared/_QuickAdd.cshtml), like Todoist's.

   While the title is typed, the server reads it (GET /api/app/parse, the same parser that creates the task): the parts it understood are marked in the text
   (a mirror behind the transparent textarea) and shown as chips under it. The x of a chip stops reading that kind (a date that is really part of the title).
   Date, priority and labels can also be picked with the buttons; what was picked by hand wins over the text. Enter adds the task, Shift+Enter is a new line,
   Escape closes the editor. Q (outside a field) opens the dialog of the quick add. Without this script the form still works: the server reads the text. */
(function () {
  'use strict';

  // the texts of the page in its language (ClientTexts: every key that starts with "Js.")
  var TEXTS = (function () { try { return JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { return {}; } })();
  function t(key) { return TEXTS[key] || key; }

  function $(sel, scope) { return (scope || document).querySelector(sel); }
  function $$(sel, scope) { return Array.prototype.slice.call((scope || document).querySelectorAll(sel)); }
  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function icon(name) { return '<svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#mat-i-' + name + '"></use></svg>'; }
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function iso(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }
  // a date picked by hand, as the chips of the server write it: today, tomorrow, the day after, else "Mon 26 Oct" / "Mo., 26. Okt." in the language of the page
  var DAY_FORMAT = new Intl.DateTimeFormat(document.documentElement.lang || 'en', { weekday: 'short', day: 'numeric', month: 'short' });
  function label(isoDate, time) {
    var p = isoDate.split('-');
    var d = new Date(+p[0], +p[1] - 1, +p[2]);
    var today = new Date(); today.setHours(0, 0, 0, 0);
    var diff = Math.round((d - today) / 86400000);
    var text = diff === 0 ? t('Js.Today') : diff === 1 ? t('Js.Tomorrow') : diff === 2 ? t('Js.DayAfterTomorrow') : DAY_FORMAT.format(d);
    return time ? text + ' ' + time : text;
  }

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
      $('[data-qa-date-label]', root).textContent = noDate.value === 'true' ? t('Js.NoDate') : due.value ? label(due.value, time.value) : t('Js.Kind.date');
      $('[data-qa-priority-label]', root).textContent = prio.value ? 'P' + prio.value : t('Js.Kind.priority');
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

    // The panels (date, priority, labels) are popovers: they lie in the top layer, above the dialog of the quick add, so nothing clips them.
    // On a wide screen they open under their button (above it when there is no room below); on a phone the CSS makes them a sheet at the bottom.
    function isOpen(p) { try { return p.matches(':popover-open'); } catch (e) { return !p.hidden; } }
    function showPop(p, btn) {
      if (p.showPopover) { if (!isOpen(p)) { p.showPopover(); } } else { p.hidden = false; }
      place(p, btn);
    }
    function hidePop(p) {
      if (p.hidePopover) { if (isOpen(p)) { p.hidePopover(); } } else { p.hidden = true; }
    }
    function place(p, btn) {
      if (window.innerWidth < 600) { p.style.left = ''; p.style.top = ''; return; }
      var r = btn.getBoundingClientRect();
      var w = p.offsetWidth, h = p.offsetHeight;
      var top = r.bottom + 4;
      if (top + h > window.innerHeight - 8 && r.top - h - 4 >= 8) { top = r.top - h - 4; }
      top = Math.max(8, Math.min(top, window.innerHeight - h - 8));
      var left = Math.max(8, Math.min(r.left, window.innerWidth - w - 8));
      p.style.top = top + 'px';
      p.style.left = left + 'px';
    }

    function closePops(except) {
      $$('[data-qa-pop]', root).forEach(function (p) {
        if (p !== except) { hidePop(p); var b = $('[aria-controls="' + p.id + '"]', root); if (b) { b.setAttribute('aria-expanded', 'false'); } }
      });
    }
    window.addEventListener('resize', function () { closePops(); });

    function reset() {
      form.reset();
      due.value = time.value = prio.value = labels.value = off.value = '';
      noDate.value = 'false';
      touched.value = 'false';
      disabled = {};
      tokens = []; parsed = null;
      $$('[data-qa-label]', root).forEach(function (c) { c.checked = false; });
      if (calendar) { setCalendar(''); timeField.value = ''; }
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

    // the buttons and their small panels
    $$('[data-qa-open]', root).forEach(function (btn) {
      btn.addEventListener('click', function () {
        var pop = $('#' + btn.getAttribute('aria-controls'));
        var open = !isOpen(pop);
        closePops(pop);
        if (open) {
          if (pop.getAttribute('data-qa-pop') === 'date') { syncCalendar(); }
          showPop(pop, btn);
          var first = $('button, input', pop);
          if (first) { first.focus(); }
        } else {
          hidePop(pop);
        }
        btn.setAttribute('aria-expanded', open ? 'true' : 'false');
      });
    });
    root.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && $$('[data-qa-pop]', root).some(isOpen)) { e.stopPropagation(); e.preventDefault(); closePops(); input.focus(); }
    });
    document.addEventListener('click', function (e) { if (!e.target.closest('.md-qa__tool')) { closePops(); } });

    function pickDate(d, t) {
      due.value = d ? iso(d) : '';
      time.value = t || '';
      noDate.value = d ? 'false' : 'true';
      setOff('date', true);
      closePops(); parse(); input.focus();
    }
    $$('[data-qa-date]', root).forEach(function (b) {
      b.addEventListener('click', function () {
        var d = new Date(); d.setHours(0, 0, 0, 0);
        var which = b.getAttribute('data-qa-date');
        if (which === 'none') { pickDate(null); return; }
        if (which === 'tomorrow') { d.setDate(d.getDate() + 1); }
        if (which === 'weekend') { var w = d.getDay(); if (w !== 6 && w !== 0) { d.setDate(d.getDate() + (6 - w)); } }
        if (which === 'nextweek') { d.setDate(d.getDate() + ((8 - d.getDay()) % 7 || 7)); }
        pickDate(d, timeField.value);
      });
    });
    // the month that is always shown (MatUi data-calendar): a click on a day takes it at once, with the time of the field above it
    var calendar = $('[data-qa-pick-date]', root);
    var timeField = $('[data-qa-pick-time]', root);
    var syncing = false;
    function setCalendar(value) {
      syncing = true;
      calendar.value = value;
      calendar.dispatchEvent(new Event('change', { bubbles: true }));
      syncing = false;
    }
    function syncCalendar() {
      setCalendar(due.value || '');
      timeField.value = time.value || '';
    }
    calendar.addEventListener('change', function () {
      if (syncing || !/^\d{4}-\d{2}-\d{2}$/.test(calendar.value)) { return; }
      var p = calendar.value.split('-');
      pickDate(new Date(+p[0], +p[1] - 1, +p[2]), timeField.value);
    });
    // a time without a day: today (like the text "um 15 Uhr"); with a day: that day at this time
    timeField.addEventListener('change', function () {
      if (!timeField.value) { time.value = ''; renderChips(); return; }
      if (!due.value) { due.value = iso(new Date()); noDate.value = 'false'; setOff('date', true); setCalendar(due.value); }
      time.value = timeField.value;
      parse();
    });
    $$('[data-qa-set-priority]', root).forEach(function (b) {
      b.addEventListener('click', function () { prio.value = b.getAttribute('data-qa-set-priority'); setOff('priority', true); closePops(); parse(); input.focus(); });
    });
    $$('[data-qa-label]', root).forEach(function (c) {
      c.addEventListener('change', function () {
        labels.value = $$('[data-qa-label]', root).filter(function (x) { return x.checked; }).map(function (x) { return x.value; }).join(',');
        renderChips();
      });
    });

    form.addEventListener('submit', function (e) {
      if (!input.value.trim()) { e.preventDefault(); input.focus(); return; }
      clearTimeout(timer);
    });

    paint();
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
