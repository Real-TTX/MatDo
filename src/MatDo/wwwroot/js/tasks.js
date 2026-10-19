/* MatDo: the task lists (Pages/Shared/_TaskList.cshtml).

   The round check sends its form in the background (POST /api/app/tasks/{id}/complete or /reopen): the row goes away, a repeating task shows its next date instead.
   Without the script the form is sent as a page (/Tasks/Add?handler=Complete). A list with data-reorder (the toolkit's drag and drop, mat:reorder) saves the new order. */
(function () {
  'use strict';

  var TEXTS = (function () { try { return JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { return {}; } })();
  function t(key, a) { return (TEXTS[key] || key).replace('{0}', a === undefined ? '' : a); }

  function token() {
    var input = document.querySelector('[data-page-token] input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
  }

  function toast(message, tone) { if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success', timeout: 4000 }); } }

  function post(url, body) {
    return fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'RequestVerificationToken': token(), 'Accept': 'application/json', 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : null
    }).then(function (r) { if (!r.ok) { throw new Error(r.status); } return r.json(); });
  }

  document.addEventListener('submit', function (e) {
    var form = e.target.closest('[data-task-toggle]');
    if (!form || !window.fetch) { return; }
    e.preventDefault();
    var row = form.closest('[data-task]');
    var id = row.getAttribute('data-task');
    var check = form.querySelector('.md-check');
    var title = (row.querySelector('.md-task__title') || {}).textContent || '';
    var reopen = row.classList.contains('is-done');
    check.disabled = true;
    check.classList.toggle('is-checked', !reopen);
    post('/api/app/tasks/' + id + (reopen ? '/reopen' : '/complete'))
      .then(function (data) {
        if (reopen) {
          row.classList.add('is-leaving');
          setTimeout(function () { row.remove(); }, 250);
          toast(t('Js.Task.Reopened', title));
        } else if (data.completed) {
          row.classList.add('is-done', 'is-leaving');
          setTimeout(function () { row.remove(); }, 300);
          toast(t('Js.Task.Completed', title));
        } else {
          // a repeating task: the next date, the row stays (or leaves the view of today)
          check.classList.remove('is-checked');
          check.disabled = false;
          var due = row.querySelector('[data-due-text]');
          if (due && data.nextDueText) { due.lastChild.textContent = data.nextDueText; due.className = 'md-due'; }
          toast(t('Js.Task.NextDue', data.nextDueText || data.nextDue));
        }
      })
      .catch(function () {
        check.disabled = false;
        check.classList.toggle('is-checked', reopen);
        form.submit();
      });
  });

  document.addEventListener('mat:reorder', function (e) {
    var order = (e.detail && e.detail.order || []).map(function (k) { return parseInt(k, 10); }).filter(function (n) { return n > 0; });
    if (order.length === 0) { return; }
    post('/api/app/tasks/reorder', { ids: order }).catch(function () { toast(t('Js.Task.OrderFailed'), 'danger'); });
  });
})();
