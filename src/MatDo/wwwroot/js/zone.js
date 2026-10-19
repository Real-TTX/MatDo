/* MatDo: the registration takes the time zone of the browser (the person can change it in the settings). */
(function () {
  'use strict';
  try {
    var zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    document.querySelectorAll('[data-detect-zone]').forEach(function (input) { if (zone) { input.value = zone; } });
  } catch (e) { /* the default stays */ }
})();
