/* MatDo: the small panels of a task (one code base for the quick add and the page of a task).
   - A button with data-pick-open and aria-controls opens its panel (data-pick-pop, popover="manual" so a dialog never clips it); a click outside, Escape or
     another panel closes it. Before it opens, the panel gets "md:pick-open" (its owner fills in the current values).
   - MatDoPick.due(panel, owner): the panel of a date (partial _DuePop): quick choices, time, repeat, the date in words, the month. owner.get() gives
     { date: '2026-10-20', time: '15:00', repeat: 'd:1', repeatName: 'Every day' }; owner.pick({ date, time }) a day (or none) was chosen, owner.time(value),
     owner.repeat(rule, name, first) a repeat ('' = none; first = its first day on or after the date, asked from /api/app/repeat).
   - MatDoPick.priority(panel, pick): the panel of the priorities (partial _PriorityPop).
   - MatDoPick.label(date, time): "Today 15:00", "Tomorrow", "Mon 26 Oct" in the language of the page. */
window.MatDoPick = (function () {
  'use strict';

  var TEXTS = (function () { try { return JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { return {}; } })();
  function t(key) { return TEXTS[key] || key; }
  function $(sel, scope) { return (scope || document).querySelector(sel); }
  function $$(sel, scope) { return Array.prototype.slice.call((scope || document).querySelectorAll(sel)); }
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function iso(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }

  // like Dates.Relative on the server: the weekday within the next six days, else the day and the month
  var WEEKDAY = new Intl.DateTimeFormat(document.documentElement.lang || 'en', { weekday: 'long' });
  var DAY_FORMAT = new Intl.DateTimeFormat(document.documentElement.lang || 'en', { day: 'numeric', month: 'short' });
  function label(isoDate, time) {
    if (!isoDate) { return ''; }
    var p = isoDate.split('-');
    var d = new Date(+p[0], +p[1] - 1, +p[2]);
    var today = new Date(); today.setHours(0, 0, 0, 0);
    var diff = Math.round((d - today) / 86400000);
    var text = diff === 0 ? t('Js.Today') : diff === 1 ? t('Js.Tomorrow') : diff === 2 ? t('Js.DayAfterTomorrow') : diff === -1 ? t('Js.Yesterday') : diff > 2 && diff < 7 ? WEEKDAY.format(d) : DAY_FORMAT.format(d);
    return time ? text + ' ' + time : text;
  }

  // ---- the panels ------------------------------------------------------------------------------------------------------------
  function isOpen(p) { try { return p.matches(':popover-open'); } catch (e) { return !p.hidden; } }
  function opener(p) { return $('[data-pick-open][aria-controls="' + p.id + '"]'); }
  function place(p, btn) {
    if (window.innerWidth < 600) { p.style.left = ''; p.style.top = ''; return; } // a sheet at the bottom (app.css)
    var r = btn.getBoundingClientRect();
    var w = p.offsetWidth, h = p.offsetHeight;
    var top = r.bottom + 4;
    if (top + h > window.innerHeight - 8 && r.top - h - 4 >= 8) { top = r.top - h - 4; }
    top = Math.max(8, Math.min(top, window.innerHeight - h - 8));
    p.style.top = top + 'px';
    p.style.left = Math.max(8, Math.min(r.left, window.innerWidth - w - 8)) + 'px';
  }
  function show(p, btn) {
    p.dispatchEvent(new CustomEvent('md:pick-open', { bubbles: true }));
    if (p.showPopover) { if (!isOpen(p)) { p.showPopover(); } } else { p.hidden = false; }
    place(p, btn);
    btn.setAttribute('aria-expanded', 'true');
    var first = $('[aria-pressed="true"], input:not(.mat-calendar__input), button', p);
    if (first) { first.focus(); }
  }
  function hide(p) {
    if (p.hidePopover) { if (isOpen(p)) { p.hidePopover(); } } else { p.hidden = true; }
    var b = opener(p);
    if (b) { b.setAttribute('aria-expanded', 'false'); }
  }
  function closeAll(except) { $$('[data-pick-pop]').forEach(function (p) { if (p !== except && isOpen(p)) { hide(p); } }); }
  function close(p, focus) { hide(p); var b = opener(p); if (focus && b) { b.focus(); } }

  document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-pick-open]');
    if (btn) {
      var p = document.getElementById(btn.getAttribute('aria-controls'));
      if (!p) { return; }
      var open = !isOpen(p);
      closeAll(p);
      if (open) { show(p, btn); } else { hide(p); }
      return;
    }
    // a click in a panel, or in the panel of a toolkit picker inside it (the time), keeps it open
    if (!e.target.closest('[data-pick-pop], .mat-picker__panel')) { closeAll(); }
  });
  // Escape closes the panel first (not the dialog of the quick add around it)
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') { return; }
    var open = $$('[data-pick-pop]').filter(isOpen);
    if (!open.length || document.querySelector('.mat-picker__panel:not([hidden])')) { return; }
    e.preventDefault(); e.stopPropagation();
    open.forEach(function (p) { close(p, true); });
  }, true);
  window.addEventListener('resize', function () { closeAll(); });

  // ---- the panel of a date -----------------------------------------------------------------------------------------------------
  function due(panel, owner) {
    var calendar = $('[data-due-calendar]', panel);
    var timeField = $('[data-due-time]', panel);
    var text = $('[data-due-text]', panel);
    var repNow = $('[data-due-repeat-now]', panel);
    var syncing = false;

    function setCalendar(value) {
      if (!calendar) { return; }
      syncing = true;
      calendar.value = value;
      calendar.dispatchEvent(new Event('change', { bubbles: true }));
      syncing = false;
    }
    // the time is a MatUi picker: set through it, so the field shows the value (its change is not taken as a choice)
    function setTime(value) {
      if (!timeField) { return; }
      syncing = true;
      if (!(window.MatUi && MatUi.picker && MatUi.picker.set(timeField, value))) { timeField.value = value; }
      syncing = false;
    }
    function sync() {
      var s = owner.get();
      setCalendar(s.date || '');
      setTime(s.time || '');
      if (repNow) { repNow.textContent = s.repeat && s.repeatName ? s.repeatName : repNow.getAttribute('data-empty'); }
    }
    function pick(date, keepOpen) {
      owner.pick({ date: date ? iso(date) : '', time: date && timeField ? timeField.value : '' });
      if (!keepOpen) { close(panel, true); }
    }

    panel.addEventListener('md:pick-open', sync);
    $$('[data-due-quick]', panel).forEach(function (b) {
      b.addEventListener('click', function () {
        var d = new Date(); d.setHours(0, 0, 0, 0);
        var which = b.getAttribute('data-due-quick');
        if (which === 'none') { setTime(''); pick(null); return; }
        if (which === 'tomorrow') { d.setDate(d.getDate() + 1); }
        if (which === 'weekend') { var w = d.getDay(); if (w !== 6 && w !== 0) { d.setDate(d.getDate() + (6 - w)); } }
        if (which === 'nextweek') { d.setDate(d.getDate() + ((8 - d.getDay()) % 7 || 7)); }
        pick(d);
      });
    });
    if (calendar) {
      calendar.addEventListener('change', function () {
        if (syncing || !/^\d{4}-\d{2}-\d{2}$/.test(calendar.value)) { return; }
        var p = calendar.value.split('-');
        pick(new Date(+p[0], +p[1] - 1, +p[2]));
      });
    }
    // a time without a day: today (like the text "at 3pm"); with a day: that day at this time
    if (timeField) {
      timeField.addEventListener('change', function () {
        if (syncing) { return; }
        if (owner.time) { owner.time(timeField.value); }
        if (timeField.value && !owner.get().date) { setCalendar(iso(new Date())); }
      });
    }
    if (text) {
      text.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); if (owner.words) { owner.words(text.value.trim()); } close(panel, true); } });
      text.addEventListener('change', function () { if (owner.words) { owner.words(text.value.trim()); } });
    }

    // ---- the repeat: a row with the rule in force; its view has the presets for the date and a rule of one's own (the server reads and names it) ----
    var repOpen = $('[data-due-repeat-open]', panel);
    var repView = $('[data-due-repeat-view]', panel);
    var st = null, previewTimer = 0, previewSeq = 0;
    function api(date, rule) {
      return fetch('/api/app/repeat?due=' + encodeURIComponent(date || '') + '&rule=' + encodeURIComponent(rule || ''), { credentials: 'same-origin', headers: { Accept: 'application/json' } })
        .then(function (r) { return r.ok ? r.json() : null; }).catch(function () { return null; });
    }
    function dateOf(s) { var p = (s || iso(new Date())).split('-'); return new Date(+p[0], +p[1] - 1, +p[2]); }
    function isoDow(d) { return d.getDay() || 7; }
    // the day the repeat starts from: the date of the owner, else the one its text says (the quick add), else today
    function base() { var s = owner.get(); return s.date || s.base || iso(new Date()); }
    // a rule as the parts of the form: "w:2:1,4;c;u=2027-01-31" = every 2 weeks on Mon and Thu, from the check-off, until 31 Jan
    function read(rule, d) {
      var s = { unit: 'w', n: 1, days: [isoDow(d)], month: String(d.getDate()), done: false, until: '' };
      if (!rule) { return s; }
      var parts = rule.split(';'), m = parts[0].split(':');
      if (m[0] === 'wd') { s.days = [1, 2, 3, 4, 5]; }
      else if (/^[dwmy]$/.test(m[0])) {
        s.unit = m[0]; s.n = Math.max(1, +m[1] || 1);
        if (s.unit === 'w' && m[2]) { s.days = m[2].split(',').map(Number); }
        if (s.unit === 'm' && m[2]) { s.month = m[2]; }
      }
      parts.slice(1).forEach(function (o) { if (o === 'c') { s.done = true; } if (o.indexOf('u=') === 0) { s.until = o.slice(2); } });
      return s;
    }
    function build(s) {
      var r = s.unit + ':' + s.n;
      if (s.unit === 'w' && s.days.length) { r += ':' + s.days.slice().sort().join(','); }
      if (s.unit === 'm' && s.month) { r += ':' + s.month; }
      return r + (s.done ? ';c' : '') + (s.until ? ';u=' + s.until : '');
    }
    function render() {
      var n = $('[data-rep-n]', panel);
      if (document.activeElement !== n) { n.value = st.n; }
      $$('[data-rep-unit]', panel).forEach(function (b) {
        var u = b.getAttribute('data-rep-unit');
        b.setAttribute('aria-pressed', u === st.unit ? 'true' : 'false');
        b.textContent = t('Js.Repeat.' + u + (st.n > 1 ? '.n' : ''));
      });
      $('[data-rep-week]', panel).hidden = st.unit !== 'w';
      $$('[data-rep-day]', panel).forEach(function (b) { b.setAttribute('aria-pressed', st.days.indexOf(+b.getAttribute('data-rep-day')) >= 0 ? 'true' : 'false'); });
      $('[data-rep-month]', panel).hidden = st.unit !== 'm';
      $$('[data-rep-part]', panel).forEach(function (b) { b.setAttribute('aria-pressed', b.getAttribute('data-rep-part') === st.month ? 'true' : 'false'); });
      $('[data-rep-done]', panel).checked = st.done;
      clearTimeout(previewTimer);
      previewTimer = setTimeout(function () {
        var id = ++previewSeq;
        api(base(), build(st)).then(function (data) { if (id === previewSeq) { $('[data-rep-preview]', panel).textContent = data && data.label ? data.label : ''; } });
      }, 120);
    }
    function option(value, label, pressed, attr) {
      var b = document.createElement('button');
      b.type = 'button'; b.className = 'md-qa__opt'; b.setAttribute(attr, value); b.setAttribute('aria-pressed', pressed ? 'true' : 'false');
      b.innerHTML = '<span></span><svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#mat-i-check"></use></svg>';
      b.firstChild.textContent = label;
      return b;
    }
    function choose(rule, name, first) {
      if (owner.repeat) { owner.repeat(rule || '', rule ? name : '', first || ''); }
      showRepeat(false); sync(); close(panel, true);
    }
    function showRepeat(on) {
      if (!repView) { return; }
      panel.classList.toggle('is-repeat', on);
      repView.hidden = !on;
      repOpen.setAttribute('aria-expanded', on ? 'true' : 'false');
      if (!on) { if (isOpen(panel)) { place(panel, opener(panel) || repOpen); } return; }
      var s = owner.get();
      st = read(s.repeat, dateOf(base()));
      var until = $('[data-rep-until]', panel);
      syncing = true;
      if (!(window.MatUi && MatUi.picker && MatUi.picker.set(until, st.until))) { until.value = st.until; }
      syncing = false;
      render();
      place(panel, opener(panel) || repOpen);
      api(base(), s.repeat).then(function (data) {
        if (!data) { return; }
        var presets = $('[data-rep-presets]', panel), month = $('[data-rep-month]', panel);
        presets.innerHTML = ''; month.innerHTML = '';
        presets.appendChild(option('', repNow.getAttribute('data-empty'), !data.rule, 'data-rep-preset'));
        var known = !data.rule;
        data.presets.forEach(function (p) { known = known || p.value === data.rule; presets.appendChild(option(p.value, p.label, p.value === data.rule, 'data-rep-preset')); });
        if (!known) { presets.appendChild(option(data.rule, data.label, true, 'data-rep-preset')); }
        // a rule of one's own opens its form, a preset or none keeps it folded
        $('[data-rep-custom]', panel).open = !known;
        data.month.forEach(function (p) { month.appendChild(option(p.part, p.label, p.part === st.month, 'data-rep-part')); });
        render();
        place(panel, opener(panel) || repOpen);
        var on = $('[data-rep-preset][aria-pressed="true"]', presets);
        if (on) { on.focus(); }
      });
    }
    if (repOpen && repView) {
      repOpen.addEventListener('click', function () { showRepeat(true); });
      $('[data-rep-back]', panel).addEventListener('click', function () { showRepeat(false); repOpen.focus(); });
      $('[data-rep-custom]', panel).addEventListener('toggle', function () { if (panel.classList.contains('is-repeat')) { place(panel, opener(panel) || repOpen); } });
      repView.addEventListener('click', function (e) {
        var preset = e.target.closest('[data-rep-preset]');
        if (preset) {
          var value = preset.getAttribute('data-rep-preset');
          if (!value) { choose('', '', ''); return; }
          api(base(), value).then(function (data) { if (data && data.rule) { choose(data.rule, data.label, data.first); } });
          return;
        }
        var unit = e.target.closest('[data-rep-unit]');
        if (unit) { st.unit = unit.getAttribute('data-rep-unit'); render(); return; }
        var day = e.target.closest('[data-rep-day]');
        if (day) {
          var d = +day.getAttribute('data-rep-day'), at = st.days.indexOf(d);
          if (at >= 0) { if (st.days.length > 1) { st.days.splice(at, 1); } } else { st.days.push(d); }
          render(); return;
        }
        var part = e.target.closest('[data-rep-part]');
        if (part) { st.month = part.getAttribute('data-rep-part'); render(); return; }
        if (e.target.closest('[data-rep-apply]')) {
          api(base(), build(st)).then(function (data) { if (data && data.rule) { choose(data.rule, data.label, data.first); } });
        }
      });
      $('[data-rep-n]', panel).addEventListener('input', function (e) { var v = parseInt(e.target.value, 10); if (v >= 1 && v <= 99) { st.n = v; render(); } });
      $('[data-rep-done]', panel).addEventListener('change', function (e) { st.done = e.target.checked; render(); });
      $('[data-rep-until]', panel).addEventListener('change', function (e) { if (syncing) { return; } st.until = /^\d{4}-\d{2}-\d{2}$/.test(e.target.value) ? e.target.value : ''; render(); });
      panel.addEventListener('md:pick-open', function () { showRepeat(false); });
    }
    return { sync: sync, setTime: setTime, setCalendar: setCalendar };
  }

  // ---- the panel of the priorities ----------------------------------------------------------------------------------------------
  function priority(panel, owner) {
    panel.addEventListener('md:pick-open', function () {
      var now = String(owner.get() || '');
      $$('[data-priority-value]', panel).forEach(function (b) { b.setAttribute('aria-pressed', b.getAttribute('data-priority-value') === now ? 'true' : 'false'); });
    });
    $$('[data-priority-value]', panel).forEach(function (b) {
      b.addEventListener('click', function () { owner.pick(b.getAttribute('data-priority-value'), b.textContent.trim()); close(panel, true); });
    });
  }

  return { due: due, priority: priority, label: label, closeAll: closeAll, close: close, isOpen: isOpen, iso: iso };
})();
