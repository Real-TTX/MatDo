/* MatDo: the page of a task (Pages/Tasks/Edit). Date, deadline and priority are fields that open the panels of the quick add (pick.js, partials _DuePop
   and _PriorityPop); what is chosen goes into the hidden fields of the form and is saved with it. The date in words (a field in the panel of the date) is
   read by the server when it saves and wins: a day chosen afterwards empties it. */
(function () {
  'use strict';

  var form = document.getElementById('task-form');
  if (!form || !window.MatDoPick) { return; }
  function field(name) { return form.querySelector('input[type="hidden"][name="' + name + '"]'); }
  function $(sel) { return form.querySelector(sel); }

  function show(out, text) {
    out.textContent = text || out.getAttribute('data-empty');
    out.classList.toggle('is-placeholder', !text);
  }
  function setIcon(btn, name, cls) {
    var svg = btn.querySelector('.mat-icon');
    if (!svg) { return; }
    svg.setAttribute('class', 'mat-icon' + (cls ? ' ' + cls : ''));
    var use = svg.querySelector('use');
    if (use && name) { use.setAttribute('href', '#mat-i-' + name); }
  }

  // ---- the date: day, time, repeat, words --------------------------------------------------------------------------------------
  var date = field('DueDate'), time = field('DueTime'), repeat = field('Recurrence');
  var dueOut = $('[data-due-value]');
  var duePop = document.getElementById('task-due');
  var words = duePop && duePop.querySelector('[data-due-text]');
  var repeatName = dueOut ? dueOut.getAttribute('data-repeat-name') || '' : '';
  function today() { return MatDoPick.iso(new Date()); }
  function showDue() {
    var text = date.value ? MatDoPick.label(date.value, time.value) : '';
    if (repeat.value && repeatName) { text = text ? text + ' · ' + repeatName : repeatName; }
    if (words && words.value.trim()) { text = words.value.trim(); }
    show(dueOut, text);
    setIcon(dueOut.closest('button'), repeat.value ? 'repeat' : 'calendar');
  }
  if (duePop && dueOut) {
    MatDoPick.due(duePop, {
      get: function () { return { date: date.value, time: time.value, repeat: repeat.value }; },
      pick: function (s) {
        date.value = s.date; time.value = s.date ? s.time : '';
        if (!s.date) { repeat.value = ''; repeatName = ''; }
        if (words) { words.value = ''; }
        showDue();
      },
      time: function (value) {
        time.value = value;
        if (value && !date.value) { date.value = today(); }
        if (words) { words.value = ''; }
        showDue();
      },
      repeat: function (value, name) {
        repeat.value = value; repeatName = value ? name : '';
        if (value && !date.value) { date.value = today(); }
        if (words) { words.value = ''; }
        showDue();
      },
      words: function () { showDue(); },
    });
  }

  // ---- the deadline: a day, without time and repeat -------------------------------------------------------------------------------
  var deadline = field('Deadline');
  var deadlineOut = $('[data-deadline-value]');
  var deadlinePop = document.getElementById('task-deadline');
  if (deadline && deadlineOut && deadlinePop) {
    MatDoPick.due(deadlinePop, {
      get: function () { return { date: deadline.value, time: '' }; },
      pick: function (s) { deadline.value = s.date; show(deadlineOut, s.date ? MatDoPick.label(s.date) : ''); },
    });
  }

  // ---- the priority --------------------------------------------------------------------------------------------------------------
  var priority = field('Priority');
  var priorityOut = $('[data-priority-value-out]');
  var priorityPop = document.getElementById('task-priority');
  if (priority && priorityOut && priorityPop) {
    MatDoPick.priority(priorityPop, {
      get: function () { return priority.value; },
      pick: function (value, name) {
        priority.value = value;
        priorityOut.textContent = name;
        setIcon(priorityOut.closest('button'), 'flag', 'md-p' + value);
      },
    });
  }
})();
