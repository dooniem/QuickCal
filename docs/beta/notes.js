/* QuickCal Web (beta) - notes on days.
   Kept apart from the calendar on purpose: app.js calls in here through a few small hooks, each
   wrapped in try/catch, so a problem with notes can never stop the calendar from working.
   Notes live in IndexedDB in this browser only (room for pasted images), and notes older than
   three months are deleted automatically. */
window.QuickCalNotes = (function () {
  'use strict';

  var DB_NAME = 'quickcal-beta-notes', STORE = 'notes';
  var KEEP_MONTHS = 3;
  var MAX_IMAGES = 4, MAX_IMAGE_SIDE = 1600;

  // Colors not used by the calendar itself (holiday red, today/week blue, birthday gold)
  var COLORS = [
    { id: 'maroon', hex: '#7b1e2b', en: 'Maroon', no: 'Vinrød' },
    { id: 'orange', hex: '#e8871e', en: 'Orange', no: 'Oransje' },
    { id: 'green', hex: '#2e9e5b', en: 'Green', no: 'Grønn' },
    { id: 'purple', hex: '#8e44ad', en: 'Purple', no: 'Lilla' },
    { id: 'teal', hex: '#11808a', en: 'Teal', no: 'Petrol' },
    { id: 'brown', hex: '#8a6a4a', en: 'Brown', no: 'Brun' }
  ];

  var deps = null;          // { T, render, showDate, isTouch, dayTitle }
  var db = null;
  var available = false;
  var index = {};           // 'YYYY-MM-DD' -> { text, color, images (count), end } for drawing the calendar
  var cover = {};           // every day a note covers -> the day it starts on (a note can span several days)
  var MAX_SPAN_DAYS = 62;
  var BOX = /^[☐☑] ?/;      // checklist line
  var editor = null;        // open editor state

  function T(en, no) { return deps.T(en, no); }
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function key(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }
  function parseKey(k) { var p = k.split('-'); return new Date(+p[0], +p[1] - 1, +p[2]); }
  function addDays(k, n) { var d = parseKey(k); d.setDate(d.getDate() + n); return key(d); }
  function colorHex(id) { for (var i = 0; i < COLORS.length; i++) if (COLORS[i].id === id) return COLORS[i].hex; return null; }

  // Each color has a number (no color = 0, then 1-6) and can be given a name, e.g. "Prosjekt A" or "Ferie".
  // The names are kept in this browser (and in the backup file).
  var NAMES_KEY = 'quickcal-beta.noteColorNames';
  var names = {};
  try { names = JSON.parse(localStorage.getItem(NAMES_KEY)) || {}; } catch (e) { names = {}; }
  function saveNames() { try { localStorage.setItem(NAMES_KEY, JSON.stringify(names)); } catch (e) { /* storage blocked */ } }
  function colorNo(id) { for (var i = 0; i < COLORS.length; i++) if (COLORS[i].id === id) return i + 1; return 0; }
  function colorDefault(id) {
    for (var i = 0; i < COLORS.length; i++) if (COLORS[i].id === id) return T(COLORS[i].en, COLORS[i].no);
    return T('No color', 'Uten farge');
  }
  function colorLabel(id) { return (names[id || 'none'] || '').trim() || colorDefault(id); }
  // A round color dot with its number inside
  function numberDot(id, cls) {
    var dot = el('span', cls || 'noteDot', String(colorNo(id)));
    var hex = colorHex(id);
    if (hex) dot.style.background = hex; else dot.classList.add('plain');
    return dot;
  }
  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  function oldestKept() {
    var t = new Date(); return key(new Date(t.getFullYear(), t.getMonth() - KEEP_MONTHS, t.getDate()));
  }

  // ---------- Storage (IndexedDB) ----------
  function openDb() {
    return new Promise(function (resolve, reject) {
      if (!window.indexedDB) { reject(new Error('no IndexedDB')); return; }
      var req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = function () { req.result.createObjectStore(STORE, { keyPath: 'date' }); };
      req.onsuccess = function () { resolve(req.result); };
      req.onerror = function () { reject(req.error); };
      req.onblocked = function () { reject(new Error('blocked')); };
    });
  }
  function tx(mode, work) {
    return new Promise(function (resolve, reject) {
      if (!db) { reject(new Error('notes not available')); return; }
      var t = db.transaction(STORE, mode), store = t.objectStore(STORE), result;
      work(store, function (r) { result = r; });
      t.oncomplete = function () { resolve(result); };
      t.onerror = t.onabort = function () { reject(t.error); };
    });
  }
  function getNote(k) {
    return tx('readonly', function (s, done) { var r = s.get(k); r.onsuccess = function () { done(r.result || null); }; });
  }
  function getAll() {
    return tx('readonly', function (s, done) { var r = s.getAll(); r.onsuccess = function () { done(r.result || []); }; });
  }
  function putNote(n) { return tx('readwrite', function (s) { s.put(n); }); }
  function deleteNote(k) { return tx('readwrite', function (s) { s.delete(k); }); }

  function isEmpty(n) { return !n || (!n.text.trim() && !n.color && !(n.images && n.images.length) && !n.repeat); }
  function remember(n) {
    if (isEmpty(n)) delete index[n.date];
    else index[n.date] = { text: n.text, color: n.color, images: (n.images || []).length, end: n.endDate && n.endDate > n.date ? n.endDate : '', repeat: n.repeat || null };
    rebuildCover();
  }
  // Multi-day notes: which note (by its first day) covers each day
  function rebuildCover() {
    cover = {};
    recurByYear = {};
    Object.keys(index).forEach(function (k) {
      if (index[k].repeat) return;   // repeating notes: see at() below
      var end = index[k].end || k, d = k;
      for (var i = 0; i < MAX_SPAN_DAYS && d <= end; i++, d = addDays(d, 1)) if (!cover[d]) cover[d] = k;
    });
  }
  function noteStart(date) { var a = at(key(date)); return a ? a.k : null; }
  // Last day a note starting on k may run to: not into the next note, and not longer than MAX_SPAN_DAYS
  function maxEnd(k) {
    var next = Object.keys(index).filter(function (s) { return s > k; }).sort()[0];
    var max = addDays(k, MAX_SPAN_DAYS - 1);
    return next && addDays(next, -1) < max ? addDays(next, -1) : max;
  }

  // ---------- Repeating notes ----------
  // A repeating note is stored once, on the day it starts from, with a rule:
  //   { freq: 'day'|'week'|'month'|'year', every: n, days: [weekdays 0-6] (week),
  //     by: 'date'|'nth' (month), nth: 1-4 or -1 for the last, wd: weekday (nth),
  //     shiftUnit: ''|'days'|'work'|'next', shift: n (days/work days, may be negative), shiftWd: weekday (next),
  //     until: 'YYYY-MM-DD' or '' }
  // Example: "4 work days after the last Sunday of the month" = month, nth -1, wd 0, work, 4.
  // The days it falls on are worked out when drawn (one year at a time) and are never stored.
  var recurByYear = {};
  function D(y, m, d) { return new Date(y, m, d); }
  function plus(d, n) { return D(d.getFullYear(), d.getMonth(), d.getDate() + n); }
  function daysIn(y, m) { return D(y, m + 1, 0).getDate(); }
  function daysBetween(a, b) { return Math.round((parseKey(b) - parseKey(a)) / 86400000); }
  function nthWeekday(y, m, nth, wd) {
    if (nth < 0) { var last = D(y, m, daysIn(y, m)); return plus(last, -((last.getDay() - wd + 7) % 7)); }
    var first = D(y, m, 1);
    return plus(first, (wd - first.getDay() + 7) % 7 + (nth - 1) * 7);
  }
  function offDay(d) { try { return deps.isOffDay ? deps.isOffDay(d) : d.getDay() % 6 === 0; } catch (e) { return false; } }
  function shifted(d, r) {
    var n = r.shift | 0;
    if (r.shiftUnit === 'days') return plus(d, n);
    if (r.shiftUnit === 'work') {
      if (!n) { for (var g = 0; g < 30 && offDay(d); g++) d = plus(d, 1); return d; }   // 0: the next work day if not one
      var step = n < 0 ? -1 : 1;
      for (var c = Math.abs(n), i = 0; c > 0 && i < 400; i++) { d = plus(d, step); if (!offDay(d)) c--; }
      return d;
    }
    if (r.shiftUnit === 'next') { var x = plus(d, 1); while (x.getDay() !== (r.shiftWd | 0)) x = plus(x, 1); return x; }
    return d;
  }
  /** The days (keys) a repeating note starting on k begins on, from..to */
  function occurrences(k, r, from, to) {
    var S = parseKey(k), out = [], every = Math.max(1, r.every | 0);
    var last = r.until && r.until < to ? r.until : to;
    if (last < from || last < k) return out;
    var stop = addDays(last, 70);   // a base day this far after may still be moved back into range
    var guard = 0;
    function push(base) {
      var o = key(shifted(base, r));
      if (o >= k && o >= from && o <= last && out.indexOf(o) < 0) out.push(o);
    }
    var skip = Math.max(0, daysBetween(k, from) - 70);   // jump ahead to near 'from'
    if (r.freq === 'day') {
      for (var i = Math.floor(skip / every); guard++ < 5000; i++) {
        var d = plus(S, i * every); if (key(d) > stop) break; push(d);
      }
    } else if (r.freq === 'week') {
      var mon = plus(S, -((S.getDay() + 6) % 7)), days = (r.days && r.days.length ? r.days : [S.getDay()]);
      for (var w = Math.floor(skip / 7 / every) * every; guard++ < 2000; w += every) {
        var wk = plus(mon, w * 7);
        if (key(wk) > stop) break;
        days.forEach(function (wd) { push(plus(wk, (wd + 6) % 7)); });
      }
    } else if (r.freq === 'month') {
      for (var m = 0; guard++ < 2000; m += every) {
        var y = S.getFullYear(), mo = S.getMonth() + m;
        var first = D(y, mo, 1);
        if (key(first) > stop) break;
        var base = r.by === 'nth' ? nthWeekday(first.getFullYear(), first.getMonth(), r.nth | 0 || 1, r.wd | 0)
                                  : D(first.getFullYear(), first.getMonth(), Math.min(S.getDate(), daysIn(first.getFullYear(), first.getMonth())));
        push(base);
      }
    } else if (r.freq === 'year') {
      for (var yr = 0; guard++ < 500; yr += every) {
        var yy = S.getFullYear() + yr;
        var b = D(yy, S.getMonth(), Math.min(S.getDate(), daysIn(yy, S.getMonth())));
        if (key(b) > stop) break;
        push(b);
      }
    }
    return out.sort();
  }
  function recurMap(y) {
    if (recurByYear[y]) return recurByYear[y];
    var map = {}, from = y + '-01-01', to = y + '-12-31';
    Object.keys(index).sort().forEach(function (k) {
      var n = index[k]; if (!n.repeat) return;
      var dur = n.end ? daysBetween(k, n.end) : 0;
      occurrences(k, n.repeat, addDays(from, -dur), to).forEach(function (o) {
        var e = addDays(o, dur);
        for (var d = o; d <= e; d = addDays(d, 1)) if (d >= from && d <= to && !cover[d] && !map[d]) map[d] = { k: k, s: o, e: e };
      });
    });
    return (recurByYear[y] = map);
  }
  /** The note on a day: { k: the day it is stored on, s/e: first/last day of this showing } */
  function at(dk) {
    var k = cover[dk];
    if (k) return { k: k, s: k, e: index[k].end || k };
    return recurMap(dk.slice(0, 4))[dk] || null;
  }
  function invalidate() { recurByYear = {}; }

  var WD_NO = ['søndag', 'mandag', 'tirsdag', 'onsdag', 'torsdag', 'fredag', 'lørdag'];
  var WD_EN = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  function wdName(i) { return T(WD_EN[i], WD_NO[i]); }
  function wdShort(i) { return T(WD_EN[i].slice(0, 2), WD_NO[i].slice(0, 2)); }
  var NTH_NO = { 1: 'første', 2: 'andre', 3: 'tredje', 4: 'fjerde', '-1': 'siste' };
  var NTH_EN = { 1: 'first', 2: 'second', 3: 'third', 4: 'fourth', '-1': 'last' };
  function shortDate(k) { var d = parseKey(k); return wdShort(d.getDay()) + '. ' + d.getDate() + '.' + (d.getMonth() + 1) + '.'; }
  /** "Every month, last Sunday + 4 work days" */
  function summary(k, r) {
    if (!r) return '';
    var S = parseKey(k), every = Math.max(1, r.every | 0), t;
    if (r.freq === 'day') t = every > 1 ? T('Every ' + every + ' days', 'Hver ' + every + '. dag') : T('Every day', 'Hver dag');
    else if (r.freq === 'week') {
      t = (every > 1 ? T('Every ' + every + ' weeks', 'Hver ' + every + '. uke') : T('Every week', 'Hver uke')) + ' ' + T('on', 'på') + ' ' +
        (r.days && r.days.length ? r.days : [S.getDay()]).slice().sort(function (a, b) { return (a + 6) % 7 - (b + 6) % 7; }).map(wdShort).join(', ');
    } else if (r.freq === 'month') {
      t = every > 1 ? T('Every ' + every + ' months', 'Hver ' + every + '. måned') : T('Every month', 'Hver måned');
      t += r.by === 'nth' ? ', ' + T(NTH_EN[r.nth] + ' ' + wdName(r.wd), NTH_NO[r.nth] + ' ' + wdName(r.wd)) : ' ' + T('on the ' + S.getDate() + '.', 'den ' + S.getDate() + '.');
    } else t = (every > 1 ? T('Every ' + every + ' years', 'Hvert ' + every + '. år') : T('Every year', 'Hvert år')) + ' ' + S.getDate() + '.' + (S.getMonth() + 1) + '.';
    var n = r.shift | 0;
    if (r.shiftUnit === 'days' && n) t += ' ' + (n > 0 ? '+ ' : '− ') + Math.abs(n) + ' ' + T(Math.abs(n) === 1 ? 'day' : 'days', Math.abs(n) === 1 ? 'dag' : 'dager');
    if (r.shiftUnit === 'work') t += n ? ' ' + (n > 0 ? '+ ' : '− ') + Math.abs(n) + ' ' + T(Math.abs(n) === 1 ? 'work day' : 'work days', Math.abs(n) === 1 ? 'arbeidsdag' : 'arbeidsdager')
                                     : T(', next work day if a day off', ', neste arbeidsdag hvis fridag');
    if (r.shiftUnit === 'next') t += T(', then the first ' + wdName(r.shiftWd) + ' after', ', så første ' + wdName(r.shiftWd) + ' etter');
    if (r.until) t += T(', until ', ', til og med ') + shortDate(r.until);
    return t;
  }
  function nextOccurrences(k, r, count) {
    var today = key(new Date());
    return occurrences(k, r, today, addDays(today, 3 * 366)).slice(0, count);
  }

  // Save, or delete when nothing is left. Asks the browser once to keep the data (not evict it).
  var askedPersist = false;
  function save(n) {
    remember(n);
    if (isEmpty(n)) return deleteNote(n.date);
    n.updated = Date.now();
    if (!askedPersist && navigator.storage && navigator.storage.persist) {
      askedPersist = true;
      navigator.storage.persist().catch(function () {});
    }
    return putNote(n);
  }

  function expired(n, limit) {
    limit = limit || oldestKept();
    if (n.repeat) return !!n.repeat.until && addDays(n.repeat.until, n.endDate ? daysBetween(n.date, n.endDate) : 0) < limit;
    return (n.endDate || n.date) < limit;
  }
  function removeOld() {
    var limit = oldestKept();
    return getAll().then(function (all) {
      var old = all.filter(function (n) { return expired(n, limit); });
      if (!old.length) return;
      return tx('readwrite', function (s) { old.forEach(function (n) { s.delete(n.date); }); })
        .then(function () { old.forEach(function (n) { delete index[n.date]; }); rebuildCover(); deps.render(); });
    });
  }

  // ---------- Images: shrink before saving, so notes stay small and fast ----------
  function shrinkImage(file) {
    return new Promise(function (resolve, reject) {
      var url = URL.createObjectURL(file), img = new Image();
      img.onload = function () {
        URL.revokeObjectURL(url);
        var s = Math.min(1, MAX_IMAGE_SIDE / Math.max(img.width, img.height));
        var c = document.createElement('canvas');
        c.width = Math.max(1, Math.round(img.width * s));
        c.height = Math.max(1, Math.round(img.height * s));
        var g = c.getContext('2d');
        g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height);
        g.drawImage(img, 0, 0, c.width, c.height);
        c.toBlob(function (b) { b ? resolve(b) : reject(new Error('could not convert image')); }, 'image/jpeg', 0.85);
      };
      img.onerror = function () { URL.revokeObjectURL(url); reject(new Error('not an image')); };
      img.src = url;
    });
  }

  // =====================================================================================
  // Calendar hooks
  // =====================================================================================

  /** Marks a day cell that has a note: color, hatch and a small notepad icon. */
  // A note over several days looks like one piece: no line between its days in the same week (and month),
  // and the notepad icon only on its last day (in each month).
  function decorate(cell, date) {
    var dk = key(date), a = at(dk), n = a && index[a.k];
    if (!n) return;
    cell.classList.add('note');
    if (n.repeat) cell.classList.add('noteRepeat');
    if (a.e !== a.s) {
      var lastOfMonth = new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate() === date.getDate();
      if (dk !== a.s && date.getDay() !== 1 && date.getDate() !== 1) cell.classList.add('noteJoinL');
      if (dk !== a.e && date.getDay() !== 0 && !lastOfMonth) cell.classList.add('noteJoinR');
      if (dk !== a.e && !lastOfMonth) cell.classList.add('noteNoIcon');
    }
    var hex = colorHex(n.color);
    if (hex && !cell.classList.contains('today') && !cell.classList.contains('holiday') && !cell.classList.contains('gold')) {
      cell.style.backgroundColor = hex;
      cell.classList.add('noteColored');
    } else if (hex) {
      cell.style.setProperty('--note-color', hex);   // keeps holiday/today look, color shows as a corner mark
      cell.classList.add('noteCorner');
    }
  }

  function hasNote(date) { return !!noteStart(date); }
  function spanText(k, end) {
    var a = parseKey(k), b = parseKey(end);
    return a.getDate() + '.' + (a.getMonth() + 1) + '. – ' + b.getDate() + '.' + (b.getMonth() + 1) + '.';
  }

  /** Hover tooltip for a day with a note: the text (and the first image), under the holiday name if any. */
  function fillTip(tip, date, holidayText, onResize) {
    var a = at(key(date)), sk = a && a.k, n = sk && index[sk];
    if (!n) return false;
    tip.innerHTML = '';
    tip.classList.add('noteTip');
    if (holidayText) tip.appendChild(el('div', 'tipHoliday', holidayText));
    if (a.e !== a.s) tip.appendChild(el('div', 'tipSpan', spanText(a.s, a.e)));
    if (n.repeat) tip.appendChild(el('div', 'tipSpan', '↻ ' + summary(sk, n.repeat)));
    var text = n.text.trim();
    if (text) tip.appendChild(el('div', 'tipText', text.length > 400 ? text.slice(0, 400) + '…' : text));
    if (n.images) {
      var holder = el('div', 'tipImages');
      tip.appendChild(holder);
      getNote(sk).then(function (full) {
        if (!full || !full.images || tip.classList.contains('hidden')) return;
        full.images.slice(0, 2).forEach(function (b) {
          var img = el('img');
          img.src = URL.createObjectURL(b);
          img.onload = function () { URL.revokeObjectURL(img.src); if (onResize) onResize(); };
          holder.appendChild(img);
        });
      }).catch(function () {});
    }
    return true;
  }

  // =====================================================================================
  // Editor: click a day, write, pick a color, paste an image. Saves by itself.
  // =====================================================================================

  function isOpen() { return !!editor; }

  // ---------- Peek: a small, non-blocking box for a clicked day, with a button that opens the editor ----------
  var peek = null;
  function openPeek(cell, date, holidayText) {
    // A second click on the same day closes it again (back to the plain calendar)
    var same = peek && peek.cell.dataset.date === cell.dataset.date && document.body.contains(peek.cell);
    closePeek();
    if (same) return;
    var k = noteStart(date) || key(date), n = index[k];
    var box = el('div', 'notePeek');
    // Just a pen button (the picked day already has a black frame), plus the holiday name if the day has one
    var open = el('button', 'npOpen', '✎');
    open.title = n ? T('Open note', 'Åpne notat') : T('Write a note', 'Skriv notat');
    open.setAttribute('aria-label', open.title);
    open.addEventListener('click', function (e) { e.stopPropagation(); closePeek(); openEditor(cell, date, holidayText); });
    box.appendChild(open);
    if (holidayText) box.appendChild(el('span', 'npHoliday', holidayText));
    box.addEventListener('click', closePeek);   // a click on the box itself (not the button) just closes it
    document.body.appendChild(box);
    cell.classList.add('picked');
    peek = { box: box, cell: cell, date: date, holidayText: holidayText };
    place(box, cell, true);
  }
  function closePeek() {
    if (!peek) return;
    peek.cell.classList.remove('picked');
    peek.box.remove();
    peek = null;
  }
  function isPeekOpen() { return !!peek; }
  // Enter on the keyboard: open the note of the picked day (the one with the small box)
  function openPicked() {
    if (!peek || !document.body.contains(peek.cell)) return false;
    var p = peek;
    openEditor(p.cell, p.date, p.holidayText);
    return true;
  }

  function openEditor(cell, date, holidayText) {
    closePeek();
    if (editor) closeEditor();
    // A day inside a multi-day note opens that note (from its first day)
    var k = noteStart(date) || key(date);
    if (k !== key(date)) { date = parseKey(k); holidayText = ''; }
    var box = el('div', 'noteEditor');
    box.setAttribute('role', 'dialog');
    var head = el('div', 'neHead');
    head.appendChild(el('span', 'neTitle', deps.dayTitle(date)));
    // – last day: a note can run over several days. The same day = a note for just that day.
    var endInput = el('input', 'neEnd'); endInput.type = 'date';
    endInput.min = k; endInput.max = maxEnd(k); endInput.value = k;
    endInput.title = T('Last day of the note', 'Siste dag for notatet');
    var dash = el('span', 'neDash', '–');
    head.appendChild(dash);
    head.appendChild(endInput);
    if (holidayText) head.appendChild(el('span', 'neHoliday', holidayText));
    var x = el('button', 'neClose', '✕'); x.title = T('Close', 'Lukk');
    head.appendChild(x);
    box.appendChild(head);

    if (!available) {
      dash.remove(); endInput.remove();
      box.appendChild(el('p', 'neInfo', T(
        'Notes are not available in this browser (private window or storage blocked).',
        'Notater er ikke tilgjengelig i denne nettleseren (privat vindu eller lagring sperret).')));
      document.body.appendChild(box);
      editor = { box: box, note: null };
      x.addEventListener('click', closeEditor);
      position(box, cell);
      return;
    }

    var area = el('textarea', 'neText');
    area.placeholder = T('Write a note…', 'Skriv et notat …');
    area.rows = 3;
    box.appendChild(area);
    var images = el('div', 'neImages');
    box.appendChild(images);

    var row = el('div', 'neRow');
    var swatches = el('div', 'neColors');
    [''].concat(COLORS.map(function (c) { return c.id; })).forEach(function (id) {
      var b = el('button', 'neSwatch' + (id ? '' : ' none'), String(colorNo(id)));
      if (id) b.style.background = colorHex(id);
      b.title = colorNo(id) + ' · ' + colorLabel(id); b.dataset.color = id;
      swatches.appendChild(b);
    });
    row.appendChild(swatches);
    var check = el('button', 'neBtn neCheck', '☑'); check.title = T('Checklist: tick box on this line', 'Sjekkliste: avkrysningsboks på denne linjen');
    row.appendChild(check);
    var rep = el('button', 'neBtn neRepeatBtn', '↻'); rep.title = T('Repeat', 'Gjenta');
    row.appendChild(rep);
    var addImg = el('button', 'neBtn', '🖼'); addImg.title = T('Add picture (or paste with Ctrl+V)', 'Legg til bilde (eller lim inn med Ctrl+V)');
    var del = el('button', 'neBtn neDelete', '🗑'); del.title = T('Delete note', 'Slett notat');
    row.appendChild(addImg);
    row.appendChild(del);
    box.appendChild(row);
    var file = el('input'); file.type = 'file'; file.accept = 'image/*'; file.multiple = true; file.hidden = true;
    box.appendChild(file);
    var panel = el('div', 'neRepeat hidden');
    box.appendChild(panel);
    var hint = el('div', 'neHint', deps.isTouch()
      ? T('Saved automatically. Kept for 3 months. ☑ makes a checklist: tap a box to tick it.',
          'Lagres automatisk. Tas vare på i 3 måneder. ☑ lager sjekkliste: trykk på en boks for å krysse av.')
      : T('Saved automatically. Paste pictures with Ctrl+V. Kept for 3 months. ☑ makes a checklist: click a box to tick it. Ctrl+Enter saves and closes.',
          'Lagres automatisk. Lim inn bilder med Ctrl+V. Tas vare på i 3 måneder. ☑ lager sjekkliste: klikk på en boks for å krysse av. Ctrl+Enter lagrer og lukker.'));
    box.appendChild(hint);

    var note = { date: k, text: '', color: '', images: [], endDate: '', repeat: null };
    editor = { box: box, note: note, timer: null, cell: cell };
    document.body.appendChild(box);
    position(box, cell);

    function showColor() {
      Array.prototype.forEach.call(swatches.children, function (b) { b.classList.toggle('on', b.dataset.color === (note.color || '')); });
    }
    function showImages() {
      images.innerHTML = '';
      note.images.forEach(function (b, i) {
        var wrap = el('span', 'neThumb');
        var img = el('img'); img.src = URL.createObjectURL(b);
        img.title = T('Show picture', 'Vis bildet');
        img.addEventListener('click', function () { showImage(b); });
        var rm = el('button', 'neThumbX', '✕'); rm.title = T('Remove picture', 'Fjern bildet');
        rm.addEventListener('click', function () { note.images.splice(i, 1); showImages(); saveSoon(0); });
        wrap.appendChild(img); wrap.appendChild(rm);
        images.appendChild(wrap);
      });
      images.classList.toggle('hidden', !note.images.length);
      position(box, cell);
    }
    function saveSoon(ms) {
      clearTimeout(editor.timer);
      editor.timer = setTimeout(function () { flush(); }, ms == null ? 400 : ms);
    }
    function addFiles(files) {
      var list = Array.prototype.filter.call(files || [], function (f) { return /^image\//.test(f.type); });
      list = list.slice(0, Math.max(0, MAX_IMAGES - note.images.length));
      if (!list.length) return false;
      Promise.all(list.map(function (f) { return shrinkImage(f).catch(function () { return null; }); })).then(function (blobs) {
        blobs.forEach(function (b) { if (b) note.images.push(b); });
        showImages(); saveSoon(0);
      });
      return true;
    }

    area.addEventListener('input', function () { note.text = area.value; saveSoon(); });
    // Checklists: a line that starts with ☐ is a task, ☑ a done task
    function setText(text, caret) {
      area.value = text; note.text = text;
      area.setSelectionRange(caret, caret);
      saveSoon(0);
    }
    check.addEventListener('click', function () {
      var v = area.value, a = area.selectionStart, b = area.selectionEnd;
      var start = v.lastIndexOf('\n', a - 1) + 1, end = v.indexOf('\n', b); if (end < 0) end = v.length;
      var lines = v.slice(start, end).split('\n');
      var off = lines.every(function (l) { return BOX.test(l); });
      var block = lines.map(function (l) { return off ? l.replace(BOX, '') : '☐ ' + l; }).join('\n');
      setText(v.slice(0, start) + block + v.slice(end), start + block.length);
      area.focus();
    });
    area.addEventListener('click', function () {   // click on a box: tick it (or untick)
      var v = area.value, p = area.selectionStart;
      if (p !== area.selectionEnd) return;
      [p, p - 1].some(function (i) {
        if (i < 0 || (v[i] !== '☐' && v[i] !== '☑') || (i > 0 && v[i - 1] !== '\n')) return false;
        setText(v.slice(0, i) + (v[i] === '☐' ? '☑' : '☐') + v.slice(i + 1), Math.min(v.length, i + 2));
        return true;
      });
    });
    area.addEventListener('keydown', function (e) {   // Enter on a task line: the next line is a task too
      if (e.key !== 'Enter' || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
      var v = area.value, p = area.selectionStart, start = v.lastIndexOf('\n', p - 1) + 1;
      var line = v.slice(start, p);
      if (!BOX.test(line) || p !== area.selectionEnd) return;
      e.preventDefault();
      if (!line.replace(BOX, '').trim()) setText(v.slice(0, start) + v.slice(p), start);   // empty task: end the list
      else setText(v.slice(0, p) + '\n☐ ' + v.slice(p), p + 3);
    });
    endInput.addEventListener('change', function () {
      var v = endInput.value;
      if (!v || v < k) v = k;
      if (v > endInput.max) v = endInput.max;
      endInput.value = v;
      note.endDate = v > k ? v : '';
      saveSoon(0);
    });
    area.addEventListener('paste', function (e) {
      var files = e.clipboardData && e.clipboardData.files;
      if (files && files.length && addFiles(files)) e.preventDefault();
    });
    box.addEventListener('dragover', function (e) { e.preventDefault(); });
    box.addEventListener('drop', function (e) { e.preventDefault(); addFiles(e.dataTransfer && e.dataTransfer.files); });
    swatches.addEventListener('click', function (e) {
      var b = e.target.closest('.neSwatch'); if (!b) return;
      note.color = b.dataset.color; showColor(); saveSoon(0);
    });
    addImg.addEventListener('click', function () { file.click(); });
    file.addEventListener('change', function () { addFiles(file.files); file.value = ''; });
    del.addEventListener('click', function () {
      note.text = ''; note.color = ''; note.images = []; note.repeat = null;
      closeEditor();
    });
    x.addEventListener('click', closeEditor);
    function showRepeat() {
      rep.classList.toggle('on', !!note.repeat);
      repeatPanel(panel, note, function () { showRepeat(); saveSoon(0); });
      position(box, cell);
    }
    rep.addEventListener('click', function () {
      panel.classList.toggle('hidden');
      hint.classList.toggle('hidden', !panel.classList.contains('hidden'));   // room for the panel in a small window
      if (!panel.classList.contains('hidden')) showRepeat(); else position(box, cell);
    });
    // Ctrl+Enter: save and close (no new line in the text)
    box.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); e.stopPropagation(); closeEditor(); }
    });

    // Focus right away (inside the click, so phones show the keyboard), so nothing typed is lost.
    // On a phone an existing note opens for reading first; tap the text to edit.
    if (!deps.isTouch() || !index[k]) area.focus();
    getNote(k).then(function (saved) {
      if (!editor || editor.note !== note) return;
      if (saved) {
        note.color = saved.color || ''; note.images = saved.images || [];
        note.endDate = saved.endDate || ''; endInput.value = note.endDate || k;
        note.repeat = saved.repeat || null;
        rep.classList.toggle('on', !!note.repeat);
        note.text = (saved.text || '') + area.value;   // keep anything typed while loading
        area.value = note.text;
      }
      showColor(); showImages();
    }).catch(function () { showColor(); });
    showColor();
  }

  // The repeat panel in the editor: a few choices that read as a sentence, and the next days it gives
  function repeatPanel(panel, note, changed) {
    var r = note.repeat, S = parseKey(note.date);
    panel.innerHTML = '';
    function select(options, value, onChange, cls) {
      var s = el('select', cls || '');
      options.forEach(function (o) { var op = el('option', '', o[1]); op.value = String(o[0]); s.appendChild(op); });
      s.value = String(value);
      s.addEventListener('change', function () { onChange(s.value); });
      return s;
    }
    function num(value, min, max, onChange) {
      var i = el('input', 'nrNum'); i.type = 'number'; i.min = min; i.max = max; i.value = value;
      i.addEventListener('change', function () {
        var v = parseInt(i.value, 10); if (isNaN(v)) v = min > 0 ? min : 0;
        v = Math.max(min, Math.min(max, v)); i.value = v; onChange(v);
      });
      return i;
    }
    function line(cls) { var l = el('div', 'nrLine' + (cls ? ' ' + cls : '')); panel.appendChild(l); return l; }
    function set(field, v) { r[field] = v; changed(); }
    var wds = [1, 2, 3, 4, 5, 6, 0];

    var l1 = line();
    l1.appendChild(el('span', '', T('Repeat', 'Gjenta')));
    l1.appendChild(select([['', T('Never', 'Aldri')], ['day', T('Daily', 'Daglig')], ['week', T('Weekly', 'Ukentlig')],
      ['month', T('Monthly', 'Månedlig')], ['year', T('Yearly', 'Årlig')]], r ? r.freq : '', function (v) {
        note.repeat = v ? { freq: v, every: 1, days: [S.getDay()], by: 'date', nth: -1, wd: S.getDay(), shiftUnit: '', shift: 0, shiftWd: 4, until: '' } : null;
        changed();
      }));
    if (!r) return;
    l1.appendChild(el('span', '', T('every', 'hver')));
    l1.appendChild(num(r.every || 1, 1, 99, function (v) { set('every', v); }));
    l1.appendChild(el('span', '', r.every > 1
      ? T({ day: 'days', week: 'weeks', month: 'months', year: 'years' }[r.freq], { day: '. dag', week: '. uke', month: '. måned', year: '. år' }[r.freq])
      : T({ day: 'day', week: 'week', month: 'month', year: 'year' }[r.freq], { day: 'dag', week: 'uke', month: 'måned', year: 'år' }[r.freq])));

    if (r.freq === 'week') {
      var l2 = line();
      wds.forEach(function (wd) {
        var b = el('button', 'nrDay' + ((r.days || []).indexOf(wd) >= 0 ? ' on' : ''), wdShort(wd));
        b.addEventListener('click', function () {
          var d = (r.days || []).slice(), i = d.indexOf(wd);
          if (i >= 0) { if (d.length > 1) d.splice(i, 1); } else d.push(wd);
          set('days', d);
        });
        l2.appendChild(b);
      });
    }
    if (r.freq === 'month') {
      var l3 = line();
      l3.appendChild(select([['date', T('on day ' + S.getDate(), 'på dag ' + S.getDate())], ['1', T('first', 'første')], ['2', T('second', 'andre')],
        ['3', T('third', 'tredje')], ['4', T('fourth', 'fjerde')], ['-1', T('last', 'siste')]], r.by === 'nth' ? r.nth : 'date', function (v) {
          if (v === 'date') r.by = 'date'; else { r.by = 'nth'; r.nth = parseInt(v, 10); }
          changed();
        }));
      if (r.by === 'nth') l3.appendChild(select(wds.map(function (wd) { return [wd, wdName(wd)]; }), r.wd, function (v) { set('wd', parseInt(v, 10)); }));
      l3.appendChild(el('span', '', T('in the month', 'i måneden')));
    }

    var l4 = line();
    l4.appendChild(el('span', '', T('Move', 'Flytt')));
    l4.appendChild(select([['', T('no', 'nei')], ['days', T('days', 'dager')], ['work', T('work days', 'arbeidsdager')],
      ['next', T('to the first', 'til første')]], r.shiftUnit || '', function (v) { r.shiftUnit = v; changed(); }));
    if (r.shiftUnit === 'days' || r.shiftUnit === 'work') l4.insertBefore(num(r.shift | 0, -60, 60, function (v) { set('shift', v); }), l4.lastChild);
    if (r.shiftUnit === 'next') {
      l4.appendChild(select(wds.map(function (wd) { return [wd, wdName(wd)]; }), r.shiftWd, function (v) { set('shiftWd', parseInt(v, 10)); }));
      l4.appendChild(el('span', '', T('after', 'etter')));
    }

    var l5 = line();
    l5.appendChild(el('span', '', T('Until', 'Til og med')));
    var until = el('input', 'neEnd'); until.type = 'date'; until.min = note.date; until.value = r.until || '';
    until.addEventListener('change', function () { set('until', until.value && until.value >= note.date ? until.value : ''); });
    l5.appendChild(until);
    if (!r.until) l5.appendChild(el('span', 'nrMuted', T('(no end)', '(uten slutt)')));

    var next = nextOccurrences(note.date, r, 4);
    var l6 = line('nrNext');
    l6.textContent = '↻ ' + summary(note.date, r) + '. ' + (next.length
      ? T('Next: ', 'Neste: ') + next.map(shortDate).join(', ')
      : T('No more days.', 'Ingen flere dager.'));
  }

  function flush() {
    if (!editor || !editor.note) return Promise.resolve();
    clearTimeout(editor.timer);
    var n = { date: editor.note.date, text: editor.note.text, color: editor.note.color, images: editor.note.images.slice() };
    if (editor.note.endDate) n.endDate = editor.note.endDate;
    if (editor.note.repeat) n.repeat = editor.note.repeat;
    return save(n).then(deps.render, function () { deps.render(); });
  }

  function closeEditor() {
    if (!editor) return;
    var ed = editor;
    flush();
    editor = null;
    Array.prototype.forEach.call(ed.box.querySelectorAll('img'), function (i) { URL.revokeObjectURL(i.src); });
    ed.box.remove();
  }

  // Next to the day on a PC, at the top of the screen on a phone (room for the keyboard)
  function position(box, cell) { place(box, cell, false); }
  function place(box, cell, small) {
    var vw = window.innerWidth, vh = window.innerHeight;
    if (!small && (deps.isTouch() || vw < 420)) {
      box.style.left = Math.max(8, (vw - box.offsetWidth) / 2) + 'px';
      box.style.top = '8px';
      return;
    }
    var r = cell.getBoundingClientRect();
    if (small) {   // under the day (above if no room), so the days beside it stay clickable
      var t = r.bottom + 4;
      if (t + box.offsetHeight > vh - 4) t = r.top - box.offsetHeight - 4;
      box.style.left = Math.max(4, Math.min(vw - box.offsetWidth - 4, r.left)) + 'px';
      box.style.top = Math.max(4, t) + 'px';
      return;
    }
    var left = r.right + 6, top = r.top;
    if (left + box.offsetWidth > vw - 4) left = r.left - box.offsetWidth - 6;
    if (left < 4) left = Math.max(4, Math.min(vw - box.offsetWidth - 4, r.left));
    if (top + box.offsetHeight > vh - 4) top = Math.max(4, vh - box.offsetHeight - 4);
    box.style.left = left + 'px';
    box.style.top = top + 'px';
  }

  function showImage(blob) {
    var cover = el('div', 'noteImageView');
    var img = el('img'); img.src = URL.createObjectURL(blob);
    cover.appendChild(img);
    cover.addEventListener('click', function () { URL.revokeObjectURL(img.src); cover.remove(); });
    document.body.appendChild(cover);
  }

  // =====================================================================================
  // List of all notes (Settings → Notes)
  // =====================================================================================

  // ---------- Notes page: search, color filter and Excel export ----------
  // Live search: notes whose text contains what is typed (any case). Color filter: none checked = all colors;
  // '' stands for notes without a color. Both apply together. Kept while the app is open.
  var filter = { q: '', colors: [] };
  function filteredKeys() {
    var q = filter.q.trim().toLowerCase();
    return Object.keys(index).sort().filter(function (k) {
      var n = index[k];
      if (filter.colors.length && filter.colors.indexOf(n.color || '') < 0) return false;
      return !q || n.text.toLowerCase().indexOf(q) >= 0;
    });
  }

  var tools = null;   // { search, filterBtn, filterMenu, exportBtn, list, empty }
  function listTools(t) {
    tools = t;
    t.search.addEventListener('input', function () { filter.q = t.search.value; refreshList(); });
    t.filterBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      t.colorsMenu.classList.add('hidden');
      if (t.filterMenu.classList.contains('hidden')) { fillFilterMenu(); t.filterMenu.classList.remove('hidden'); }
      else t.filterMenu.classList.add('hidden');
    });
    t.filterMenu.addEventListener('click', function (e) { e.stopPropagation(); });
    document.addEventListener('click', function () { t.filterMenu.classList.add('hidden'); });
    t.exportBtn.addEventListener('click', exportList);
    t.backupBtn.title = backupTitle();
    t.backupBtn.addEventListener('click', backup);
    t.restoreBtn.title = T('Restore notes from a backup file', 'Gjenopprett notater fra en sikkerhetskopi');
    t.restoreBtn.addEventListener('click', function () { if (available) t.restoreFile.click(); });
    t.restoreFile.addEventListener('change', function () {
      var f = t.restoreFile.files && t.restoreFile.files[0];
      t.restoreFile.value = '';
      if (f) restore(f).then(function () { if (t.onRestored) t.onRestored(); });
    });
    // Color names: a small menu with the numbered colors and a name field for each
    t.colorsBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      t.filterMenu.classList.add('hidden');
      if (t.colorsMenu.classList.contains('hidden')) { fillColorsMenu(); t.colorsMenu.classList.remove('hidden'); }
      else t.colorsMenu.classList.add('hidden');
    });
    t.colorsMenu.addEventListener('click', function (e) { e.stopPropagation(); });
    document.addEventListener('click', function () {
      if (t.colorsMenu.classList.contains('hidden')) return;
      t.colorsMenu.classList.add('hidden');
      refreshList();
    });
  }
  function fillFilterMenu() {
    var m = tools.filterMenu;
    m.innerHTML = '';
    function row(label, dotHex, on, plain, onClick, colorId) {
      var r = el('button', 'nfRow' + (on ? ' on' : ''));
      r.appendChild(el('span', 'nfCheck', on ? '✓' : ''));
      if (dotHex !== null) r.appendChild(numberDot(plain ? '' : colorId));
      r.appendChild(el('span', '', label));
      r.addEventListener('click', function () { onClick(); fillFilterMenu(); refreshList(); });
      m.appendChild(r);
    }
    row(T('All colors', 'Alle farger'), null, !filter.colors.length, false, function () { filter.colors = []; });
    COLORS.concat([{ id: '', en: 'No color', no: 'Uten farge' }]).forEach(function (c) {
      var on = filter.colors.indexOf(c.id) >= 0;
      row(colorLabel(c.id), c.hex || '', on, !c.id, function () {
        if (on) filter.colors = filter.colors.filter(function (x) { return x !== c.id; });
        else filter.colors = filter.colors.concat([c.id]);
      }, c.id);
    });
  }
  function fillColorsMenu() {
    var m = tools.colorsMenu;
    m.innerHTML = '';
    m.appendChild(el('div', 'ncTitle', T('Name the colors', 'Gi fargene navn')));
    [''].concat(COLORS.map(function (c) { return c.id; })).forEach(function (id) {
      var r = el('label', 'ncRow');
      r.appendChild(numberDot(id));
      var input = el('input'); input.type = 'text'; input.maxLength = 40;
      input.placeholder = colorDefault(id); input.value = names[id || 'none'] || '';
      input.addEventListener('input', function () {
        if (input.value.trim()) names[id || 'none'] = input.value; else delete names[id || 'none'];
        saveNames();
      });
      input.addEventListener('keydown', function (e) { if (e.key === 'Enter') { tools.colorsMenu.classList.add('hidden'); refreshList(); } });
      r.appendChild(input);
      m.appendChild(r);
    });
  }
  function refreshList() { if (tools) fillList(tools.list, tools.empty); }

  // Excel: the list as shown (search and color filter), with the pictures placed in their own cells
  function exportList() {
    var keys = filteredKeys();
    if (!keys.length) return;
    var save = window.QuickCalXlsx.target('QuickCal-' + T('notes', 'notater') + '-' + key(new Date()) + '.xlsx');
    save.catch(function () {});   // cancelled: nothing to do
    tools.exportBtn.disabled = true;
    getAll().then(function (all) {
      var byDate = {};
      all.forEach(function (n) { byDate[n.date] = n; });
      var maxPics = 0;
      keys.forEach(function (k) { maxPics = Math.max(maxPics, ((byDate[k] && byDate[k].images) || []).length); });
      var head = [T('Date', 'Dato'), T('To', 'Til'), T('Day', 'Dag'), T('Week', 'Uke'), T('No.', 'Nr'), T('Color', 'Farge'), T('Note', 'Notat'), T('Repeats', 'Gjentas')];
      for (var i = 0; i < maxPics; i++) head.push(T('Picture ', 'Bilde ') + (i + 1));
      var rows = [head], images = [], rowHeights = {}, jobs = [];
      keys.forEach(function (k, r) {
        var n = byDate[k] || { text: index[k].text, color: index[k].color, images: [] }, d = parseKey(k);
        var row = [d, n.endDate ? parseKey(n.endDate) : '', cap(d.toLocaleDateString(lang(), { weekday: 'long' })), deps.isoWeek(d), colorNo(n.color), colorLabel(n.color), (n.text || '').trim(), summary(k, n.repeat)];
        for (var j = 0; j < maxPics; j++) row.push('');
        rows.push(row);
        (n.images || []).forEach(function (blob, j) {
          rowHeights[r + 1] = 90;
          jobs.push(picture(blob).then(function (p) {
            if (p) images.push({ row: r + 1, col: 8 + j, data: p.data, type: p.type, w: p.w, h: p.h, name: k + ' ' + (j + 1) });
          }));
        });
      });
      return Promise.all(jobs).then(function () {
        var widths = [10, 10, 10, 6, 5, 14, 50, 24];
        for (var i = 0; i < maxPics; i++) widths.push(22);
        var blob = window.QuickCalXlsx.write(rows, { sheet: T('Notes', 'Notater'), widths: widths, wrap: [6, 7], images: images, rowHeights: rowHeights });
        return save.then(function (write) { return write(blob); });
      });
    }).catch(function (e) { if (window.console && !(e && e.name === 'AbortError')) console.error(e); })
      .then(function () { tools.exportBtn.disabled = !filteredKeys().length; });
  }
  function picture(blob) {
    return Promise.all([blob.arrayBuffer(), createImageBitmap(blob)]).then(function (r) {
      var p = { data: new Uint8Array(r[0]), type: blob.type === 'image/png' ? 'image/png' : 'image/jpeg', w: r[1].width, h: r[1].height };
      if (r[1].close) r[1].close();
      return p;
    }).catch(function () { return null; });
  }
  function lang() { return T('en', 'nb'); }
  function cap(t) { return t.charAt(0).toUpperCase() + t.slice(1); }

  function fillList(listEl, emptyEl) {
    listEl.innerHTML = '';
    var all = Object.keys(index).length, keys = filteredKeys();
    emptyEl.classList.toggle('hidden', all > 0);
    if (tools) {
      var active = filter.colors.length > 0;
      tools.filterBtn.classList.toggle('active', active);
      tools.filterBtn.querySelector('.nfCount').textContent = active ? String(filter.colors.length) : '';
      tools.exportBtn.disabled = !keys.length;
    }
    showBackupHint();
    if (all && !keys.length) listEl.appendChild(el('div', 'noteNoMatch', T('No notes match.', 'Ingen notater passer.')));
    keys.forEach(function (k) {
      var n = index[k], d = parseKey(k);
      var row = el('div', 'noteRow');
      var dot = numberDot(n.color);
      dot.title = colorLabel(n.color);
      row.appendChild(dot);
      var dateCell = el('span', 'noteDate', deps.dayTitle(d, true));
      if (n.repeat) {
        var nx = nextOccurrences(k, n.repeat, 1)[0];
        dateCell.textContent = '↻ ' + (nx ? deps.dayTitle(parseKey(nx), true) : deps.dayTitle(d, true));
        dateCell.title = summary(k, n.repeat);
      }
      if (n.end && !n.repeat) { var e = parseKey(n.end); dateCell.appendChild(el('span', 'noteEnd', ' → ' + e.getDate() + '.' + (e.getMonth() + 1) + '.')); }
      row.appendChild(dateCell);
      var first = n.text.trim().split('\n')[0] || (n.images ? T('(picture)', '(bilde)') : '');
      row.appendChild(el('span', 'noteFirst', first));
      var tasks = n.text.match(/^[☐☑]/gm);
      if (tasks) row.appendChild(el('span', 'noteTasks', '☑ ' + tasks.filter(function (t) { return t === '☑'; }).length + '/' + tasks.length));
      if (n.images) row.appendChild(el('span', 'noteImgs', '🖼' + (n.images > 1 ? n.images : '')));
      var rm = el('button', 'noteRemove', '🗑'); rm.title = T('Delete note', 'Slett notat');
      rm.addEventListener('click', function (e) {
        e.stopPropagation();
        save({ date: k, text: '', color: '', images: [] }).then(function () { deps.render(); fillList(listEl, emptyEl); });
      });
      row.appendChild(rm);
      row.addEventListener('click', function () { deps.showDate(d); });
      listEl.appendChild(row);
    });
  }

  // ---------- Backup: all notes, pictures and color names in one file, and restore from it ----------
  // A .json file rather than Excel: it brings everything back exactly as it was (pictures, colors, several
  // days, checklists), and nobody is tempted to edit it. The Excel export is for reading and sharing.
  var BACKUP_KIND = 'quickcal-notes-backup', LAST_BACKUP_KEY = 'quickcal-beta.lastNotesBackup';
  function blobToData(b) {
    return new Promise(function (resolve) {
      var r = new FileReader();
      r.onload = function () { resolve(r.result); };
      r.onerror = function () { resolve(null); };
      r.readAsDataURL(b);
    });
  }
  function dataToBlob(d) { return fetch(d).then(function (r) { return r.blob(); }).catch(function () { return null; }); }
  function lastBackup() { try { return localStorage.getItem(LAST_BACKUP_KEY) || ''; } catch (e) { return ''; } }
  function backupTitle() {
    var last = lastBackup();
    return T('Back up all notes to a file', 'Ta sikkerhetskopi av alle notatene til en fil') +
      (last ? ' (' + T('last: ', 'sist: ') + deps.dayTitle(parseKey(last), true) + ')' : '');
  }
  function backup() {
    var save = window.QuickCalXlsx.target('QuickCal-' + T('notes-backup', 'notater-sikkerhetskopi') + '-' + key(new Date()) + '.json',
      [{ description: 'QuickCal', accept: { 'application/json': ['.json'] } }]);
    save.catch(function () {});
    getAll().then(function (all) {
      return Promise.all(all.map(function (n) {
        return Promise.all((n.images || []).map(blobToData)).then(function (imgs) {
          var out = { date: n.date, text: n.text || '', color: n.color || '', updated: n.updated || 0 };
          if (n.endDate) out.endDate = n.endDate;
          if (n.repeat) out.repeat = n.repeat;
          out.images = imgs.filter(Boolean);
          return out;
        });
      })).then(function (list) {
        var file = { kind: BACKUP_KIND, version: 1, created: new Date().toISOString(), colorNames: names, notes: list };
        var blob = new Blob([JSON.stringify(file, null, 1)], { type: 'application/json' });
        return save.then(function (write) { return write(blob); });
      });
    }).then(function () {
      try { localStorage.setItem(LAST_BACKUP_KEY, key(new Date())); } catch (e) { /* storage blocked */ }
      if (tools) { tools.backupBtn.title = backupTitle(); showBackupHint(); }
    }).catch(function (e) { if (window.console && !(e && e.name === 'AbortError')) console.error(e); });
  }
  // A small reminder next to the button: notes live only in this browser
  function showBackupHint() {
    var h = tools && tools.backupHint; if (!h) return;
    var last = lastBackup();
    h.textContent = T('Notes are only stored here. Back up regularly with ↓', 'Notatene lagres bare her. Ta sikkerhetskopi jevnlig med ↓') +
      (last ? ' (' + T('last ', 'sist ') + parseKey(last).getDate() + '.' + (parseKey(last).getMonth() + 1) + '.)' : '.');
    h.classList.toggle('hidden', !Object.keys(index).length);
  }
  // Notes in the file replace notes that start on the same day; all other notes are kept.
  function restore(file) {
    var bad = T('This file is not a QuickCal notes backup.', 'Denne filen er ikke en sikkerhetskopi av QuickCal-notater.');
    return file.text().then(function (text) {
      var data;
      try { data = JSON.parse(text); } catch (e) { data = null; }
      if (!data || data.kind !== BACKUP_KIND || !Array.isArray(data.notes)) { alert(bad); return; }
      var valid = data.notes.filter(function (n) { return n && /^\d{4}-\d\d-\d\d$/.test(n.date) && typeof n.text === 'string'; });
      var keep = valid.filter(function (n) { return !expired(n); });
      var old = valid.length - keep.length;
      if (!keep.length) {
        alert(old ? T('All notes in the backup are older than 3 months, so none were restored.',
                      'Alle notatene i sikkerhetskopien er eldre enn 3 måneder, så ingen ble gjenopprettet.')
                  : T('The backup has no notes.', 'Sikkerhetskopien har ingen notater.'));
        return;
      }
      var created = data.created ? new Date(data.created) : null;
      if (!confirm(T('Restore ' + keep.length + ' notes from the backup' + (created ? ' of ' + created.toLocaleDateString('en') : '') + '?\n\nNotes on the same day are replaced, all other notes are kept.',
                     'Gjenopprette ' + keep.length + ' notater fra sikkerhetskopien' + (created ? ' fra ' + created.toLocaleDateString('nb') : '') + '?\n\nNotater på samme dag blir erstattet, alle andre notater beholdes.'))) return;
      return Promise.all(keep.map(function (n) {
        return Promise.all((n.images || []).slice(0, MAX_IMAGES).map(dataToBlob)).then(function (blobs) {
          var r = { date: n.date, text: n.text, color: colorHex(n.color) ? n.color : '', images: blobs.filter(Boolean), updated: n.updated || Date.now() };
          if (n.endDate && n.endDate > n.date) r.endDate = n.endDate;
          if (n.repeat) r.repeat = n.repeat;
          return r;
        });
      })).then(function (records) {
        return tx('readwrite', function (s) { records.forEach(function (r) { s.put(r); }); });
      }).then(function () {
        if (data.colorNames && typeof data.colorNames === 'object') {
          Object.keys(data.colorNames).forEach(function (k) { if (typeof data.colorNames[k] === 'string' && data.colorNames[k].trim()) names[k] = data.colorNames[k]; });
          saveNames();
        }
        return getAll();
      }).then(function (all) {
        index = {}; all.forEach(remember); deps.render();
        alert(T(keep.length + ' notes restored.', keep.length + ' notater er gjenopprettet.') +
          (old ? ' ' + T(old + ' older than 3 months were left out.', old + ' eldre enn 3 måneder ble utelatt.') : ''));
      });
    }).catch(function (e) { if (window.console) console.error(e); alert(bad); });
  }

  function deleteAll() {
    return tx('readwrite', function (s) { s.clear(); }).then(function () { index = {}; cover = {}; deps.render(); });
  }

  // =====================================================================================

  function init(d) {
    deps = d;
    return openDb().then(function (opened) {
      db = opened;
      available = true;
      return getAll();
    }).then(function (all) {
      all.forEach(remember);
      deps.render();
      return removeOld();
    }).catch(function () { available = false; });
  }

  return {
    init: init,
    decorate: decorate,
    hasNote: hasNote,
    fillTip: fillTip,
    openEditor: openEditor,
    closeEditor: closeEditor,
    isOpen: isOpen,
    openPeek: openPeek,
    openPicked: openPicked,
    closePeek: closePeek,
    isPeekOpen: isPeekOpen,
    fillList: fillList,
    listTools: listTools,
    deleteAll: deleteAll,
    count: function () { return Object.keys(index).length; },
    reload: function () {   // notes may have been changed in the "Always on top" window
      if (!available) return Promise.resolve();
      return getAll().then(function (all) { index = {}; all.forEach(remember); rebuildCover(); deps.render(); });
    },
    invalidate: invalidate,
    removeOld: function () { return available ? removeOld() : Promise.resolve(); }
  };
})();
