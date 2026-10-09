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
  var index = {};           // 'YYYY-MM-DD' -> { text, color, images (count) } for drawing the calendar
  var editor = null;        // open editor state

  function T(en, no) { return deps.T(en, no); }
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function key(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }
  function parseKey(k) { var p = k.split('-'); return new Date(+p[0], +p[1] - 1, +p[2]); }
  function colorHex(id) { for (var i = 0; i < COLORS.length; i++) if (COLORS[i].id === id) return COLORS[i].hex; return null; }
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
    else index[n.date] = { text: n.text, color: n.color, images: (n.images || []).length };
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
      var old = all.filter(function (n) { return n.date < limit; });
      if (!old.length) return;
      return tx('readwrite', function (s) { old.forEach(function (n) { s.delete(n.date); }); })
        .then(function () { old.forEach(function (n) { delete index[n.date]; }); deps.render(); });
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
  function decorate(cell, date) {
    var n = index[key(date)];
    if (!n) return;
    cell.classList.add('note');
    var hex = colorHex(n.color);
    if (hex && !cell.classList.contains('today') && !cell.classList.contains('holiday') && !cell.classList.contains('gold')) {
      cell.style.backgroundColor = hex;
      cell.classList.add('noteColored');
    } else if (hex) {
      cell.style.setProperty('--note-color', hex);   // keeps holiday/today look, color shows as a corner mark
      cell.classList.add('noteCorner');
    }
  }

  function hasNote(date) { return !!index[key(date)]; }

  /** Hover tooltip for a day with a note: the text (and the first image), under the holiday name if any. */
  function fillTip(tip, date, holidayText, onResize) {
    var n = index[key(date)];
    if (!n) return false;
    tip.innerHTML = '';
    tip.classList.add('noteTip');
    if (holidayText) tip.appendChild(el('div', 'tipHoliday', holidayText));
    var text = n.text.trim();
    if (text) tip.appendChild(el('div', 'tipText', text.length > 400 ? text.slice(0, 400) + '…' : text));
    if (n.images) {
      var holder = el('div', 'tipImages');
      tip.appendChild(holder);
      getNote(key(date)).then(function (full) {
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
    var k = key(date), n = index[k];
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
    peek = { box: box, cell: cell };
    place(box, cell, true);
  }
  function closePeek() {
    if (!peek) return;
    peek.cell.classList.remove('picked');
    peek.box.remove();
    peek = null;
  }
  function isPeekOpen() { return !!peek; }

  function openEditor(cell, date, holidayText) {
    closePeek();
    if (editor) closeEditor();
    var k = key(date);
    var box = el('div', 'noteEditor');
    box.setAttribute('role', 'dialog');
    var head = el('div', 'neHead');
    head.appendChild(el('span', 'neTitle', deps.dayTitle(date)));
    if (holidayText) head.appendChild(el('span', 'neHoliday', holidayText));
    var x = el('button', 'neClose', '✕'); x.title = T('Close', 'Lukk');
    head.appendChild(x);
    box.appendChild(head);

    if (!available) {
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
    var none = el('button', 'neSwatch none'); none.title = T('No color', 'Ingen farge'); none.dataset.color = '';
    swatches.appendChild(none);
    COLORS.forEach(function (c) {
      var b = el('button', 'neSwatch'); b.style.background = c.hex; b.title = T(c.en, c.no); b.dataset.color = c.id;
      swatches.appendChild(b);
    });
    row.appendChild(swatches);
    var addImg = el('button', 'neBtn', '🖼'); addImg.title = T('Add picture (or paste with Ctrl+V)', 'Legg til bilde (eller lim inn med Ctrl+V)');
    var del = el('button', 'neBtn neDelete', '🗑'); del.title = T('Delete note', 'Slett notat');
    row.appendChild(addImg);
    row.appendChild(del);
    box.appendChild(row);
    var file = el('input'); file.type = 'file'; file.accept = 'image/*'; file.multiple = true; file.hidden = true;
    box.appendChild(file);
    var hint = el('div', 'neHint', deps.isTouch()
      ? T('Saved automatically. Kept for 3 months.', 'Lagres automatisk. Tas vare på i 3 måneder.')
      : T('Saved automatically. Paste pictures with Ctrl+V. Kept for 3 months.', 'Lagres automatisk. Lim inn bilder med Ctrl+V. Tas vare på i 3 måneder.'));
    box.appendChild(hint);

    var note = { date: k, text: '', color: '', images: [] };
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

    // Focus right away (inside the click, so phones show the keyboard), so nothing typed is lost.
    // On a phone an existing note opens for reading first; tap the text to edit.
    if (!deps.isTouch() || !index[k]) area.focus();
    getNote(k).then(function (saved) {
      if (!editor || editor.note !== note) return;
      if (saved) {
        note.color = saved.color || ''; note.images = saved.images || [];
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

  function fillList(listEl, emptyEl) {
    listEl.innerHTML = '';
    var keys = Object.keys(index).sort();
    emptyEl.classList.toggle('hidden', keys.length > 0);
    keys.forEach(function (k) {
      var n = index[k], d = parseKey(k);
      var row = el('div', 'noteRow');
      var dot = el('span', 'noteDot');
      var hex = colorHex(n.color);
      if (hex) dot.style.background = hex; else dot.classList.add('plain');
      row.appendChild(dot);
      row.appendChild(el('span', 'noteDate', deps.dayTitle(d, true)));
      var first = n.text.trim().split('\n')[0] || (n.images ? T('(picture)', '(bilde)') : '');
      row.appendChild(el('span', 'noteFirst', first));
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
    return tx('readwrite', function (s) { s.clear(); }).then(function () { index = {}; deps.render(); });
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
    closePeek: closePeek,
    isPeekOpen: isPeekOpen,
    fillList: fillList,
    deleteAll: deleteAll,
    count: function () { return Object.keys(index).length; },
    reload: function () {   // notes may have been changed in the "Always on top" window
      if (!available) return Promise.resolve();
      return getAll().then(function (all) { index = {}; all.forEach(remember); deps.render(); });
    },
    removeOld: function () { return available ? removeOld() : Promise.resolve(); }
  };
})();
