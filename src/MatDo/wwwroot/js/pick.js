/* MatDo: the small panels of a task (one code base for the quick add and the page of a task).
   - A button with data-pick-open and aria-controls opens its panel (data-pick-pop, popover="manual" so a dialog never clips it); a click outside, Escape or
     another panel closes it. Before it opens, the panel gets "md:pick-open" (its owner fills in the current values).
   - MatDoPick.due(panel, owner): the panel of a date (partial _DuePop): quick choices, time, repeat, the date in words, the month. owner.get() gives
     { date: '2026-10-20', time: '15:00', repeat: 'd:1' }; owner.pick({ date, time }) a day (or none) was chosen, owner.time(value), owner.repeat(value).
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
      $$('[data-due-repeat]', panel).forEach(function (b) { b.setAttribute('aria-pressed', b.getAttribute('data-due-repeat') === (s.repeat || '') ? 'true' : 'false'); });
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
    $$('[data-due-repeat]', panel).forEach(function (b) {
      b.addEventListener('click', function () { if (owner.repeat) { owner.repeat(b.getAttribute('data-due-repeat'), b.textContent.trim()); } sync(); close(panel, true); });
    });
    if (text) {
      text.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); if (owner.words) { owner.words(text.value.trim()); } close(panel, true); } });
      text.addEventListener('change', function () { if (owner.words) { owner.words(text.value.trim()); } });
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
