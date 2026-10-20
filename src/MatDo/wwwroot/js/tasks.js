/* MatDo: the task lists (Pages/Shared/_TaskList.cshtml) – checking off, drag and drop, swipes, long press.

   - The round check sends its form in the background (POST /api/app/tasks/{id}/complete or /reopen): the row goes away, a repeating task shows its next date.
     Without the script the form is sent as a page (/Tasks/Add?handler=Complete).
   - A list with data-reorder (the toolkit's drag and drop, mat:reorder) saves the new order; in a tree a task moves with its sub-tasks.
   - A tree (data-depth): a task with sub-tasks folds them (data-subtoggle, remembered in localStorage md-folded); checking off or deleting takes them along.
   - Swipes (the toolkit's sliding rows, mat:item-action): to the right = complete, to the left = tomorrow / delete.
   - A long press on a phone, a right click or the menu key on a computer opens the menu of the row (_TaskMenu.cshtml): complete, a date, a priority, edit, delete.
     The menu is a popover (above a dialog, closed by a tap outside or Escape); on a phone it is a sheet at the bottom. */
(function () {
  'use strict';

  var TEXTS = (function () { try { return JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { return {}; } })();
  function t(key, a) { return (TEXTS[key] || key).replace('{0}', a === undefined ? '' : a); }

  function toast(message, tone) { if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success', timeout: 4000 }); } }

  // a message that has to survive the reload of the page (a row that changed its date may belong to another day)
  function toastAfterReload(message) { try { sessionStorage.setItem('md-toast', message); } catch (e) { /* private mode */ } location.reload(); }
  (function () { try { var m = sessionStorage.getItem('md-toast'); if (m) { sessionStorage.removeItem('md-toast'); setTimeout(function () { toast(m); }, 50); } } catch (e) { /* private mode */ } })();

  // every change goes through the queue of the offline mode (offline.js): sent now, or later when the network is back; the page shows it at once either way
  function post(url, body, label) {
    if (window.MatDoOffline) {
      return window.MatDoOffline.send(url, body, label).then(function (r) { var d = r.data || {}; d.queued = r.queued; return d; });
    }
    return fetch(url, {
      method: 'POST', credentials: 'same-origin',
      headers: { 'X-MatDo-Request': '1', 'Accept': 'application/json', 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : null
    }).then(function (r) { if (!r.ok) { throw new Error(r.status); } return r.json(); });
  }
  function offlineNote(message, queued) { return queued ? message + ' ' + t('Js.Offline.Later') : message; }

  function titleOf(row) { var el = row.querySelector('.md-task__title'); return el ? el.textContent.trim() : ''; }

  function leave(row) {
    // a task takes its sub-tasks along (they are done or deleted with it on the server)
    below(row).concat([row]).forEach(function (r) {
      r.classList.add('is-leaving');
      setTimeout(function () { r.remove(); }, 280);
    });
  }

  // ---- the tree: a sub-task stands under its task (data-depth); the rows under a row are the next ones that are deeper ----
  function depthOf(row) { return parseInt(row.getAttribute('data-depth'), 10) || 0; }
  function below(row) {
    var out = [], d = depthOf(row), next = row.nextElementSibling;
    while (next && next.matches('[data-task]') && depthOf(next) > d) { out.push(next); next = next.nextElementSibling; }
    return out;
  }
  var FOLD_KEY = 'md-folded';
  function folded() { try { return JSON.parse(localStorage.getItem(FOLD_KEY) || '[]'); } catch (e) { return []; } }
  function setFold(btn, open, remember) {
    var row = btn.closest('[data-task]');
    btn.setAttribute('aria-expanded', open ? 'true' : 'false');
    btn.setAttribute('aria-label', btn.getAttribute(open ? 'data-label-open' : 'data-label-closed'));
    // showing a task shows the rows under it, except those under a sub-task that is folded itself
    var hideFrom = null;
    below(row).forEach(function (r) {
      if (hideFrom !== null && depthOf(r) <= hideFrom) { hideFrom = null; }
      r.hidden = !open || hideFrom !== null;
      var inner = r.querySelector('[data-subtoggle]');
      if (open && hideFrom === null && inner && inner.getAttribute('aria-expanded') === 'false') { hideFrom = depthOf(r); }
    });
    if (remember) {
      var id = row.getAttribute('data-task');
      var list = folded().filter(function (x) { return x !== id; });
      if (!open) { list.push(id); }
      try { localStorage.setItem(FOLD_KEY, JSON.stringify(list.slice(-500))); } catch (e) { /* private mode */ }
    }
  }
  document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-subtoggle]');
    if (btn) { setFold(btn, btn.getAttribute('aria-expanded') === 'false', true); }
  });
  (function () {
    var list = folded();
    if (!list.length) { return; }
    Array.prototype.forEach.call(document.querySelectorAll('[data-subtoggle]'), function (btn) {
      if (list.indexOf(btn.closest('[data-task]').getAttribute('data-task')) >= 0) { setFold(btn, false, false); }
    });
  })();

  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function iso(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }
  function dayFor(which) {
    var d = new Date(); d.setHours(0, 0, 0, 0);
    if (which === 'tomorrow') { d.setDate(d.getDate() + 1); }
    if (which === 'weekend') { var w = d.getDay(); if (w !== 6 && w !== 0) { d.setDate(d.getDate() + (6 - w)); } }
    if (which === 'nextweek') { d.setDate(d.getDate() + ((8 - d.getDay()) % 7 || 7)); }
    return which === 'none' ? '' : iso(d);
  }

  // ---- the actions, shared by the check, the swipes and the menu ----------------------------------------------------
  function complete(row) {
    var id = row.getAttribute('data-task');
    var title = titleOf(row);
    var check = row.querySelector('.md-check');
    if (check) { check.classList.add('is-checked'); }
    if (navigator.vibrate) { navigator.vibrate(10); }
    return post('/api/app/tasks/' + id + '/complete', null, title).then(function (data) {
      if (data.completed || data.queued) {
        row.classList.add('is-done');
        leave(row);
        toast(offlineNote(t('Js.Task.Completed', title), data.queued));
      } else {
        // a repeating task: its next date; the row stays (or belongs to another day: the page shows it there after a reload)
        if (check) { check.classList.remove('is-checked'); }
        toastAfterReload(t('Js.Task.NextDue', data.nextDueText || data.nextDue));
      }
    });
  }

  function reopen(row) {
    var id = row.getAttribute('data-task');
    var title = titleOf(row);
    return post('/api/app/tasks/' + id + '/reopen', null, title).then(function (data) { leave(row); toast(offlineNote(t('Js.Task.Reopened', title), data.queued)); });
  }

  function setDue(row, which) {
    var id = row.getAttribute('data-task');
    return post('/api/app/tasks/' + id + '/due', { date: dayFor(which) }, titleOf(row)).then(function (data) {
      if (data.queued) {
        // without a network the page cannot move the row to its new day: it says what will happen
        toast(offlineNote(which === 'none' ? t('Js.NoDate') : t('Js.Task.Due', t('Js.Due.' + which)), true));
        return;
      }
      toastAfterReload(data.dueText ? t('Js.Task.Due', data.dueText) : t('Js.NoDate'));
    });
  }

  function setPriority(row, p) {
    var id = row.getAttribute('data-task');
    return post('/api/app/tasks/' + id + '/priority', { priority: +p }, titleOf(row)).then(function (data) {
      row.setAttribute('data-priority', p);
      var check = row.querySelector('.md-check');
      if (check) { check.className = check.className.replace(/\bmd-p[1-4]\b/, 'md-p' + p); }
      toast(offlineNote(t('Js.Task.Priority', p), data.queued));
    });
  }

  function remove(row) {
    var id = row.getAttribute('data-task');
    var title = titleOf(row);
    return post('/api/app/tasks/' + id + '/delete', null, title).then(function (data) { leave(row); toast(offlineNote(t('Js.Task.Deleted', title), data.queued)); });
  }

  function failed() { toast(t('Js.Task.Failed'), 'danger'); }

  // ---- the check of a row --------------------------------------------------------------------------------------------
  document.addEventListener('submit', function (e) {
    var form = e.target.closest('[data-task-toggle]');
    if (!form || !window.fetch) { return; }
    e.preventDefault();
    var row = form.closest('[data-task]');
    var check = form.querySelector('.md-check');
    check.disabled = true;
    (row.classList.contains('is-done') ? reopen(row) : complete(row))
      .catch(function () { check.disabled = false; form.submit(); })
      .then(function () { check.disabled = false; });
  });

  // ---- drag and drop ---------------------------------------------------------------------------------------------------
  // In a tree a task moves with its sub-tasks: after the drop each task gets the rows under it back, in their new order (a row dropped into
  // another task's block goes back after it); the order that is saved is the order of the page then.
  function keepTree(list) {
    var rows = Array.prototype.slice.call(list.querySelectorAll(':scope > [data-task]'));
    if (!rows.some(function (r) { return depthOf(r) > 0; })) { return null; }
    var byParent = {};
    rows.forEach(function (r) { var p = r.getAttribute('data-parent') || ''; (byParent[p] = byParent[p] || []).push(r); });
    var ids = rows.map(function (r) { return r.getAttribute('data-task'); });
    var ordered = [];
    function add(r) { ordered.push(r); (byParent[r.getAttribute('data-task')] || []).forEach(add); }
    rows.filter(function (r) { var p = r.getAttribute('data-parent'); return !p || ids.indexOf(p) < 0; }).forEach(add);
    ordered.forEach(function (r) { list.appendChild(r); });
    return ordered.map(function (r) { return parseInt(r.getAttribute('data-task'), 10); });
  }

  document.addEventListener('mat:reorder', function (e) {
    var list = e.target && e.target.closest ? e.target.closest('.md-tasks') : null;
    var tree = list ? keepTree(list) : null;
    var order = (tree || (e.detail && e.detail.order) || []).map(function (k) { return parseInt(k, 10); }).filter(function (n) { return n > 0; });
    if (order.length === 0) { return; }
    post('/api/app/tasks/reorder', { ids: order }).catch(function () { toast(t('Js.Task.OrderFailed'), 'danger'); });
  });

  // ---- swipes (the toolkit slides the row and asks before delete: data-confirm on the option) ----------------------------
  document.addEventListener('mat:item-action', function (e) {
    var row = e.detail && e.detail.item;
    if (!row || !row.hasAttribute('data-task')) { return; }
    var action = e.detail.action;
    var done = action === 'complete' ? complete(row) : action === 'tomorrow' ? setDue(row, 'tomorrow') : action === 'delete' ? remove(row) : null;
    if (done) { done.catch(failed); }
  });

  // ---- the menu of a row: long press, right click, menu key ----------------------------------------------------------
  var menu = document.getElementById('md-task-menu');
  var menuRow = null;

  function openMenu(row, x, y) {
    if (!menu || !menu.showPopover) { return; }
    menuRow = row;
    menu.querySelector('[data-task-menu-title]').textContent = titleOf(row);
    var edit = menu.querySelector('[data-task-act="edit"]');
    edit.setAttribute('href', '/Tasks/Edit?id=' + row.getAttribute('data-task'));
    var done = row.classList.contains('is-done');
    menu.querySelector('[data-task-act="complete"]').hidden = done;
    var p = row.getAttribute('data-priority');
    Array.prototype.forEach.call(menu.querySelectorAll('[data-task-act="priority"]'), function (b) { b.setAttribute('aria-checked', b.getAttribute('data-priority') === p ? 'true' : 'false'); });
    if (menu.matches(':popover-open')) { menu.hidePopover(); }
    menu.showPopover();
    if (window.innerWidth >= 600) {
      var w = menu.offsetWidth, h = menu.offsetHeight;
      menu.style.left = Math.max(8, Math.min(x, window.innerWidth - w - 8)) + 'px';
      menu.style.top = Math.max(8, Math.min(y, window.innerHeight - h - 8)) + 'px';
    } else {
      menu.style.left = ''; menu.style.top = '';
    }
    var first = menu.querySelector('[data-task-act]:not([hidden])');
    if (first) { first.focus(); }
  }

  if (menu) {
    menu.addEventListener('click', function (e) {
      var b = e.target.closest('[data-task-act]');
      if (!b || !menuRow) { return; }
      var row = menuRow;
      var act = b.getAttribute('data-task-act');
      if (act === 'edit') { return; }
      e.preventDefault();
      menu.hidePopover();
      var job = null;
      if (act === 'complete') { job = complete(row); }
      if (act === 'due') { job = setDue(row, b.getAttribute('data-due')); }
      if (act === 'priority') { job = setPriority(row, b.getAttribute('data-priority')); }
      if (act === 'delete') {
        var ask = window.MatUi && window.MatUi.confirm
          ? window.MatUi.confirm(row.querySelector('[data-item-action="delete"]') ? row.querySelector('[data-item-action="delete"]').getAttribute('data-confirm') : t('Js.Task.DeleteConfirm'), { ok: t('Js.Delete'), danger: true })
          : Promise.resolve(true);
        Promise.resolve(ask).then(function (yes) { if (yes) { remove(row).catch(failed); } });
        return;
      }
      if (job) { job.catch(failed); }
    });
    menu.addEventListener('toggle', function (e) { if (e.newState === 'closed') { menuRow = null; } });

    // the menu closes by a tap or click beside it and by Escape (a manual popover: the tap that ends a long press must not close it at once)
    var openedAt = 0;
    menu.addEventListener('toggle', function (e) { if (e.newState === 'open') { openedAt = Date.now(); } });
    document.addEventListener('pointerdown', function (e) {
      if (menu.matches(':popover-open') && !menu.contains(e.target) && Date.now() - openedAt > 300) { menu.hidePopover(); }
    }, true);
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && menu.matches(':popover-open')) { e.preventDefault(); menu.hidePopover(); }
    });
  }

  function rowOf(target) {
    var row = target.closest && target.closest('.md-task[data-task]');
    if (!row || target.closest('.mat-item__options, .mat-item__handle, .md-task__check')) { return null; }
    return row;
  }

  // right click on a computer (and the context menu event of Android's long press)
  document.addEventListener('contextmenu', function (e) {
    var row = rowOf(e.target);
    if (!row || !menu) { return; }
    e.preventDefault();
    openMenu(row, e.clientX, e.clientY);
  });

  // the menu key / Shift+F10 on a focused row
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'ContextMenu' && !(e.key === 'F10' && e.shiftKey)) { return; }
    var row = rowOf(document.activeElement || document.body);
    if (!row) { return; }
    e.preventDefault();
    var r = row.getBoundingClientRect();
    openMenu(row, r.left + 24, r.top + r.height / 2);
  });

  // long press with a finger (iOS does not send contextmenu for a link): 450 ms without moving
  var press = null;
  document.addEventListener('pointerdown', function (e) {
    if (e.pointerType !== 'touch') { return; }
    var row = rowOf(e.target);
    if (!row) { return; }
    var x = e.clientX, y = e.clientY;
    press = { row: row, x: x, y: y, fired: false, timer: setTimeout(function () {
      press.fired = true;
      if (navigator.vibrate) { navigator.vibrate(15); }
      row.classList.add('is-pressed');
      setTimeout(function () { row.classList.remove('is-pressed'); }, 200);
      openMenu(row, x, y);
    }, 450) };
  }, { passive: true });
  function cancelPress(e) {
    if (!press) { return; }
    if (e && e.type === 'pointermove' && Math.abs(e.clientX - press.x) < 8 && Math.abs(e.clientY - press.y) < 8) { return; }
    clearTimeout(press.timer);
    if (!press.fired) { press = null; }
  }
  document.addEventListener('pointermove', cancelPress, { passive: true });
  document.addEventListener('pointercancel', cancelPress);
  document.addEventListener('pointerup', function () { if (press && !press.fired) { clearTimeout(press.timer); press = null; } });
  // the tap that ends a long press does not open the task
  document.addEventListener('click', function (e) {
    if (press && press.fired) {
      if (rowOf(e.target) === press.row) { e.preventDefault(); e.stopPropagation(); }
      press = null;
    }
  }, true);
})();
