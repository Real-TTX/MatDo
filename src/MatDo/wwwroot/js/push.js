/* Notifications of this device (the settings page /Account/Notifications): asks the browser, subscribes it at its push service with the key of the app
   and hands the subscription to the server (/api/app/push/subscribe); off ends it on both sides. The states:
   - unsupported: the browser has no Web Push;
   - ios: an iPhone or iPad gets notifications only from the app on the home screen (iOS 16.4 and later), not from Safari;
   - denied: notifications were blocked in the browser; only its settings can allow them again;
   - off / on: the buttons for it. With a subscription the server is told again on every visit (it may have dropped it). */
(function () {
  'use strict';

  var root = document.querySelector('[data-push]');
  if (!root) { return; }
  var state = root.querySelector('[data-push-state]');
  var on = root.querySelector('[data-push-on]');
  var off = root.querySelector('[data-push-off]');
  var test = root.querySelector('[data-push-test]');
  var texts = {};
  try { texts = JSON.parse(document.getElementById('md-texts').textContent); } catch (e) { /* the keys are shown */ }
  function t(key, a) { var s = texts[key] || key; return a === undefined ? s : s.replace('{0}', a); }

  function show(text, buttons) {
    state.textContent = text;
    on.hidden = buttons.indexOf('on') < 0;
    off.hidden = buttons.indexOf('off') < 0;
    test.hidden = buttons.indexOf('test') < 0;
  }

  function post(url, body) {
    return fetch(url, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json', 'Content-Type': 'application/json', 'X-MatDo-Request': '1' },
      body: JSON.stringify(body || {}),
    }).then(function (r) { if (!r.ok) { throw new Error(String(r.status)); } return r.json(); });
  }

  function send(subscription) {
    var json = subscription.toJSON();
    return post('/api/app/push/subscribe', { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth });
  }

  function key(base64) {
    var padded = (base64 + '==='.slice(0, (4 - base64.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
    var raw = atob(padded);
    var out = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) { out[i] = raw.charCodeAt(i); }
    return out;
  }

  var ios = /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  var standalone = window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
  var supported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window && window.isSecureContext;

  if (!supported) { show(t(ios && !standalone ? 'Js.Push.Ios' : 'Js.Push.Unsupported'), []); return; }
  if (Notification.permission === 'denied') { show(t('Js.Push.Denied'), []); return; }

  var registration = null;
  navigator.serviceWorker.ready.then(function (reg) {
    registration = reg;
    return reg.pushManager.getSubscription();
  }).then(function (subscription) {
    if (subscription && Notification.permission === 'granted') {
      show(t('Js.Push.On'), ['test', 'off']);
      send(subscription).catch(function () { /* told on the next visit */ });
    } else {
      show(t('Js.Push.Off'), ['on']);
    }
  }).catch(function () { show(t('Js.Push.Unsupported'), []); });

  on.addEventListener('click', function () {
    on.disabled = true;
    // the question of the browser must come from the click itself (Safari)
    Notification.requestPermission().then(function (permission) {
      if (permission !== 'granted') { throw new Error(permission); }
      return fetch('/api/app/push/key', { credentials: 'same-origin', headers: { 'Accept': 'application/json' } }).then(function (r) { return r.json(); });
    }).then(function (data) {
      return registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key(data.key) });
    }).then(send).then(function () {
      location.reload(); // the device shows in the list
    }).catch(function (e) {
      on.disabled = false;
      if (Notification.permission === 'denied') { show(t('Js.Push.Denied'), []); } else { show(t('Js.Push.Failed') + (e && e.message ? ' (' + e.message + ')' : ''), ['on']); }
    });
  });

  off.addEventListener('click', function () {
    off.disabled = true;
    registration.pushManager.getSubscription().then(function (subscription) {
      if (!subscription) { return null; }
      var endpoint = subscription.endpoint;
      return subscription.unsubscribe().then(function () { return post('/api/app/push/unsubscribe', { endpoint: endpoint }); });
    }).then(function () { location.reload(); }, function () { off.disabled = false; show(t('Js.Push.Failed'), ['test', 'off']); });
  });

  test.addEventListener('click', function () {
    test.disabled = true;
    post('/api/app/push/test').then(function (data) {
      show(t('Js.Push.Sent', data.sent), ['test', 'off']);
    }, function () { show(t('Js.Push.Failed'), ['test', 'off']); }).then(function () { test.disabled = false; });
  });
})();
