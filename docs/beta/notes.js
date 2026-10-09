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

  function isEmpty(n) { return !n || (!n.text.trim() && !n.color && !(n.images && n.images.length)); }
  function remember(n) {
    if (isEmpty(n)) delete index[n.date];
    else index[n.date] = { text: n.text, color: n.color, images: (n.images || []).length, end: n.endDate && n.endDate > n.date ? n.endDate : '' };
    rebuildCover();
  }
  // Multi-day notes: which note (by its first day) covers each day
  function rebuildCover() {
    cover = {};
    Object.keys(index).forEach(function (k) {
      var end = index[k].end || k, d = k;
      for (var i = 0; i < MAX_SPAN_DAYS && d <= end; i++, d = addDays(d, 1)) if (!cover[d]) cover[d] = k;
    });
  }
  function noteStart(date) { return cover[key(date)] || null; }
  // Last day a note starting on k may run to: not into the next note, and not longer than MAX_SPAN_DAYS
  function maxEnd(k) {
    var next = Object.keys(index).filter(function (s) { return s > k; }).sort()[0];
    var max = addDays(k, MAX_SPAN_DAYS - 1);
    return next && addDays(next, -1) < max ? addDays(next, -1) : max;
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

  function removeOld() {
    var limit = oldestKept();
    return getAll().then(function (all) {
      var old = all.filter(function (n) { return (n.endDate || n.date) < limit; });
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
    var dk = key(date), sk = cover[dk], n = sk && index[sk];
    if (!n) return;
    cell.classList.add('note');
    if (n.end) {
      var lastOfMonth = new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate() === date.getDate();
      if (dk !== sk && date.getDay() !== 1 && date.getDate() !== 1) cell.classList.add('noteJoinL');
      if (dk !== n.end && date.getDay() !== 0 && !lastOfMonth) cell.classList.add('noteJoinR');
      if (dk !== n.end && !lastOfMonth) cell.classList.add('noteNoIcon');
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
    var sk = noteStart(date), n = sk && index[sk];
    if (!n) return false;
    tip.innerHTML = '';
    tip.classList.add('noteTip');
    if (holidayText) tip.appendChild(el('div', 'tipHoliday', holidayText));
    if (n.end) tip.appendChild(el('div', 'tipSpan', spanText(sk, n.end)));
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
    var addImg = el('button', 'neBtn', '🖼'); addImg.title = T('Add picture (or paste with Ctrl+V)', 'Legg til bilde (eller lim inn med Ctrl+V)');
    var del = el('button', 'neBtn neDelete', '🗑'); del.title = T('Delete note', 'Slett notat');
    row.appendChild(addImg);
    row.appendChild(del);
    box.appendChild(row);
    var file = el('input'); file.type = 'file'; file.accept = 'image/*'; file.multiple = true; file.hidden = true;
    box.appendChild(file);
    var hint = el('div', 'neHint', deps.isTouch()
      ? T('Saved automatically. Kept for 3 months. ☑ makes a checklist: tap a box to tick it.',
          'Lagres automatisk. Tas vare på i 3 måneder. ☑ lager sjekkliste: trykk på en boks for å krysse av.')
      : T('Saved automatically. Paste pictures with Ctrl+V. Kept for 3 months. ☑ makes a checklist: click a box to tick it. Ctrl+Enter saves and closes.',
          'Lagres automatisk. Lim inn bilder med Ctrl+V. Tas vare på i 3 måneder. ☑ lager sjekkliste: klikk på en boks for å krysse av. Ctrl+Enter lagrer og lukker.'));
    box.appendChild(hint);

    var note = { date: k, text: '', color: '', images: [], endDate: '' };
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
      note.text = ''; note.color = ''; note.images = [];
      closeEditor();
    });
    x.addEventListener('click', closeEditor);
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
        note.text = (saved.text || '') + area.value;   // keep anything typed while loading
        area.value = note.text;
      }
      showColor(); showImages();
    }).catch(function () { showColor(); });
    showColor();
  }

  function flush() {
    if (!editor || !editor.note) return Promise.resolve();
    clearTimeout(editor.timer);
    var n = { date: editor.note.date, text: editor.note.text, color: editor.note.color, images: editor.note.images.slice() };
    if (editor.note.endDate) n.endDate = editor.note.endDate;
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
      var head = [T('Date', 'Dato'), T('To', 'Til'), T('Day', 'Dag'), T('Week', 'Uke'), T('No.', 'Nr'), T('Color', 'Farge'), T('Note', 'Notat')];
      for (var i = 0; i < maxPics; i++) head.push(T('Picture ', 'Bilde ') + (i + 1));
      var rows = [head], images = [], rowHeights = {}, jobs = [];
      keys.forEach(function (k, r) {
        var n = byDate[k] || { text: index[k].text, color: index[k].color, images: [] }, d = parseKey(k);
        var row = [d, n.endDate ? parseKey(n.endDate) : '', cap(d.toLocaleDateString(lang(), { weekday: 'long' })), deps.isoWeek(d), colorNo(n.color), colorLabel(n.color), (n.text || '').trim()];
        for (var j = 0; j < maxPics; j++) row.push('');
        rows.push(row);
        (n.images || []).forEach(function (blob, j) {
          rowHeights[r + 1] = 90;
          jobs.push(picture(blob).then(function (p) {
            if (p) images.push({ row: r + 1, col: 7 + j, data: p.data, type: p.type, w: p.w, h: p.h, name: k + ' ' + (j + 1) });
          }));
        });
      });
      return Promise.all(jobs).then(function () {
        var widths = [10, 10, 10, 6, 5, 14, 50];
        for (var i = 0; i < maxPics; i++) widths.push(22);
        var blob = window.QuickCalXlsx.write(rows, { sheet: T('Notes', 'Notater'), widths: widths, wrap: [6], images: images, rowHeights: rowHeights });
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
    if (all && !keys.length) listEl.appendChild(el('div', 'noteNoMatch', T('No notes match.', 'Ingen notater passer.')));
    keys.forEach(function (k) {
      var n = index[k], d = parseKey(k);
      var row = el('div', 'noteRow');
      var dot = numberDot(n.color);
      dot.title = colorLabel(n.color);
      row.appendChild(dot);
      var dateCell = el('span', 'noteDate', deps.dayTitle(d, true));
      if (n.end) { var e = parseKey(n.end); dateCell.appendChild(el('span', 'noteEnd', ' → ' + e.getDate() + '.' + (e.getMonth() + 1) + '.')); }
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
    removeOld: function () { return available ? removeOld() : Promise.resolve(); }
  };
})();
