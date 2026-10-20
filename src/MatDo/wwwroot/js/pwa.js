/* MatDo as an app (PWA): the service worker, the number on the app icon, the shortcuts of the app icon and the share target.
   - The number on the app icon (Badging API) is the count of the menu: the open tasks of today and the overdue ones (data-due-count); a notification sets it too (sw.js).
   - /Today?quick=1 (the shortcut "Add task" of the app icon) opens the quick add.
   - Text shared from another app (manifest share_target: /Today?share_title=&share_text=&share_url=) opens the quick add with that text. */
(function () {
  'use strict';

  if ('serviceWorker' in navigator && window.isSecureContext) {
    window.addEventListener('load', function () { navigator.serviceWorker.register('/sw.js').catch(function () { /* the app works without it */ }); });
  }

  var due = document.querySelector('[data-due-count]');
  if (due && 'setAppBadge' in navigator) {
    var count = parseInt(due.getAttribute('data-due-count'), 10) || 0;
    (count ? navigator.setAppBadge(count) : navigator.clearAppBadge()).catch(function () { /* not allowed here */ });
  }

  var params = new URLSearchParams(location.search);
  var shared = [params.get('share_title'), params.get('share_text'), params.get('share_url')]
    .filter(function (v) { return v && v.trim(); })
    .filter(function (v, i, all) { return all.indexOf(v) === i; })
    .join(' ');
  if (!params.has('quick') && !shared) { return; }

  var dialog = document.getElementById('md-quick');
  if (!dialog) { return; }
  window.addEventListener('load', function () {
    if (!dialog.open) { dialog.showModal(); }
    var input = dialog.querySelector('[data-qa-input]');
    if (input) {
      if (shared) {
        input.value = shared;
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      input.focus();
    }
    // the address loses the parameters: a reload does not open the dialog again
    ['quick', 'share_title', 'share_text', 'share_url'].forEach(function (k) { params.delete(k); });
    var rest = params.toString();
    history.replaceState(null, '', location.pathname + (rest ? '?' + rest : ''));
  });
})();
