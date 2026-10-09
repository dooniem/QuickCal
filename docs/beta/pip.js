/* QuickCal beta: "Always on top".
   Edge and Chrome on a PC can show a page in a small floating window that stays above other windows
   (Document Picture-in-Picture). The calendar is shown there and this window only says where it went,
   so there are never two calendars. Always off at start: it needs a click every time.
   To remove the feature: delete this file, its <script> tag and the pipBtn1 button in index.html,
   and the .pipBtn/.pipCover/html.pip styles. app.js only needs ?pip to work as before. */
(function () {
  'use strict';
  function no() { return document.documentElement.lang === 'no'; }
  function T(en, nb) { return no() ? nb : en; }
  function hoverText(b) { b.addEventListener('mouseenter', function () { b.title = T('Always on top', 'Alltid øverst'); }); }

  // In the floating window itself: the same button, green while it is on. A click turns it off.
  if (/[?&]pip(=|&|$)/.test(location.search)) {
    document.addEventListener('DOMContentLoaded', function () {
      var b = document.getElementById('pipBtn1');
      if (!b) return;
      b.classList.remove('hidden');
      b.classList.add('pipOn');
      hoverText(b);
      b.addEventListener('click', function (e) {
        e.stopPropagation();
        try { window.parent.postMessage('quickcal-pip-close', location.origin); } catch (err) { /* ignore */ }
      });
    });
    return;
  }
  if (!('documentPictureInPicture' in window)) return;           // other browsers, phones and tablets
  if (window.matchMedia && matchMedia('(hover: none) and (pointer: coarse)').matches) return;

  var pipWin = null;
  function installed() {
    return !!window.matchMedia && (matchMedia('(display-mode: standalone)').matches || matchMedia('(display-mode: window-controls-overlay)').matches);
  }

  function open() {
    if (pipWin) { pipWin.focus(); return; }
    documentPictureInPicture.requestWindow({ width: 600, height: 220 }).then(function (win) {
      pipWin = win;
      var d = win.document;
      d.title = 'QuickCal';
      var style = d.createElement('style');
      style.textContent = 'html,body{margin:0;height:100%;overflow:hidden;background:#fff}iframe{border:0;width:100%;height:100%;display:block}';
      d.head.appendChild(style);
      var frame = d.createElement('iframe');
      frame.src = new URL('index.html?pip' + (installed() ? '=notes' : ''), location.href).href;
      frame.addEventListener('load', function () { try { frame.contentWindow.focus(); } catch (e) { /* ignore */ } });
      d.body.appendChild(frame);
      win.addEventListener('pagehide', closed);
      // The green button: back to this window. A click in the floating window counts as a click here too, so
      // the browser lets this window come to the front (it would otherwise stay behind the others).
      win.addEventListener('message', function (e) {
        if (e.origin !== location.origin || e.data !== 'quickcal-pip-close') return;
        unshrink();
        try { window.focus(); } catch (err) { /* ignore */ }
        win.close();
      });
      showCover();
      shrink();
    }).catch(function (e) { if (window.console) console.error(e); });
  }

  // Installed app: make this window as small as the browser allows while the calendar floats, and give
  // it its size back afterwards. A web page cannot minimize its own window, and a browser tab cannot be resized.
  var savedSize = null;
  function shrink() {
    if (!installed()) return;
    savedSize = { w: window.outerWidth, h: window.outerHeight };
    try { window.resizeTo(1, 1); } catch (e) { savedSize = null; }
  }
  function unshrink() {
    if (!savedSize) return;
    var s = savedSize;
    savedSize = null;
    try { window.resizeTo(s.w, s.h); } catch (e) { /* ignore */ }
  }

  function closed() {
    pipWin = null;
    unshrink();
    try { window.focus(); } catch (e) { /* ignore */ }
    document.documentElement.classList.remove('pipActive');
    var c = document.getElementById('pipCover');
    if (c) c.remove();
    if (window.QuickCalApp) window.QuickCalApp.refresh();
  }

  // This window while the calendar floats: a short note and a button to bring it back
  function showCover() {
    document.documentElement.classList.add('pipActive');
    var c = document.createElement('div');
    c.id = 'pipCover';
    var p = document.createElement('p');
    p.textContent = T('QuickCal is in its own window, on top of the others.', 'QuickCal ligger i et eget vindu, øverst over de andre.');
    var b = document.createElement('button');
    b.className = 'linkBox';
    b.textContent = T('Bring it back here', 'Hent den tilbake hit');
    b.addEventListener('click', function () { if (pipWin) pipWin.close(); });
    c.appendChild(p);
    c.appendChild(b);
    document.body.appendChild(c);
  }

  // The calendar's keys must not work here while it floats
  window.addEventListener('keydown', function (e) {
    if (pipWin) e.stopImmediatePropagation();
  }, true);

  document.addEventListener('DOMContentLoaded', function () {
    // Only on the 3-month page (pipBtn1): the floating window always shows the 3 months, never the whole year
    var b = document.getElementById('pipBtn1');
    if (!b) return;
    b.classList.remove('hidden');
    hoverText(b);
    b.addEventListener('click', function (e) { e.stopPropagation(); open(); });
  });
})();
