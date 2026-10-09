/* QuickCal Web - ISO week calendar with Norwegian holidays.
   Same features as the Windows version, adapted to what a web app can do:
   the week number is shown as a badge on the taskbar icon (installed app), not by the clock. */
(function () {
  'use strict';

  // =====================================================================================
  // Settings (saved in the browser)
  // =====================================================================================

  var STORAGE_KEY = 'quickcal.settings';

  // "Always on top" (pip.js) shows this page in a small floating window, opened with ?pip
  var PIP = /[?&]pip(=|&|$)/.test(location.search);
  var DEFAULTS = {
    language: 'UseSystemLanguage', // 'English' | 'Norwegian' | 'UseSystemLanguage'
    showHolidays: true,
    easterFullWeek: true,
    summerWeeks: 3,
    appSize: null,          // last normal window size of the installed app {w, h} (inner size)
    appMaximized: false,    // installed app was last left maximized
    appAutostartShown: false
  };

  function loadSettings() {
    try {
      var raw = localStorage.getItem(STORAGE_KEY);
      return Object.assign({}, DEFAULTS, raw ? JSON.parse(raw) : {});
    } catch (e) {
      return Object.assign({}, DEFAULTS);
    }
  }
  function saveSettings() {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(settings)); } catch (e) { /* private mode etc. */ }
  }
  var settings = loadSettings();

  // =====================================================================================
  // Language and texts
  // =====================================================================================

  // The browser's own language (Intl follows it) counts too: Edge in Norwegian with English first
  // among the preferred web page languages should still give a Norwegian QuickCal.
  function systemLang() {
    var first = (navigator.languages && navigator.languages[0]) || navigator.language || '';
    var ui = '';
    try { ui = Intl.DateTimeFormat().resolvedOptions().locale; } catch (e) { /* old browser */ }
    if (!isNorwegianCode(first) && isNorwegianCode(ui)) return ui;
    return first || ui || 'en';
  }
  function isNorwegianCode(code) {
    code = (code || '').toLowerCase();
    return code === 'no' || code.indexOf('nb') === 0 || code.indexOf('nn') === 0 || code.indexOf('no-') === 0;
  }
  function norwegian() {
    if (settings.language === 'Norwegian') return true;
    if (settings.language === 'UseSystemLanguage') return isNorwegianCode(systemLang());
    return false;
  }
  function T(en, no) { return norwegian() ? no : en; }
  function cap(s) { return s ? s.charAt(0).toLocaleUpperCase(systemLang()) + s.slice(1) : s; }

  var EN_MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
  var NO_MONTHS = ['Januar', 'Februar', 'Mars', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Desember'];

  function monthNames() {
    if (settings.language === 'Norwegian') return NO_MONTHS;
    if (settings.language === 'English') return EN_MONTHS;
    try {
      // Month names in the system language, any language (no hard-coding)
      var f = new Intl.DateTimeFormat(systemLang(), { month: 'long' });
      var result = [];
      for (var i = 0; i < 12; i++) result.push(cap(f.format(new Date(2001, i, 1))));
      return result;
    } catch (e) {
      return EN_MONTHS;
    }
  }

  /** Column headers: week label, then Monday ... Sunday */
  function dayNames() {
    if (settings.language === 'Norwegian') return ['Uke', 'Ma', 'Ti', 'On', 'To', 'Fr', 'Lø', 'Sø'];
    if (settings.language === 'English') return ['Week', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];
    var result = [norwegian() ? 'Uke' : 'Week'];
    try {
      var f = new Intl.DateTimeFormat(systemLang(), { weekday: 'short' });
      for (var i = 0; i < 7; i++) {
        var name = f.format(new Date(2001, 0, 1 + i)).replace(/\.+$/, ''); // 1 Jan 2001 was a Monday
        result.push(cap(Array.from(name).slice(0, 2).join('')));      // "man." -> "Ma", "Mon" -> "Mo"
      }
    } catch (e) {
      return ['Week', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];
    }
    return result;
  }

  var HOLIDAY_TEXTS = {
    fixed: [['New Year’s Day', '1. Nyttårsdag'], ['Labour Day', 'Arbeidernes dag'], ['Constitution Day', 'Grunnlovsdagen'], ['New Year’s Eve', 'Nyttårsaften']],
    christmas: [['Christmas break', 'Lillejulaften'], ['Christmas Eve', 'Julaften'], ['Christmas Day', '1. Juledag'], ['Boxing Day', '2. Juledag'], ['Christmas break', 'Juleferie']],
    easter: [['Maundy Thursday', 'Skjærtorsdag'], ['Good Friday', 'Langfredag'], ['Easter Eve', 'Påskeaften'], ['Easter Sunday', '1. påskedag'], ['Easter Monday', '2. påskedag'], ['Easter', 'Påske']],
    related: [['Ascension Day', 'Kristi himmelfartsdag'], ['Pentecost', '1. Pinsedag'], ['Whit Monday', '2. Pinsedag']],
    summer: ['Summer break', 'Sommerferie'],
    birthday: ['Developer’s birthday', 'Utviklerens bursdag']
  };
  function tx(pair) { return T(pair[0], pair[1]); }

  // =====================================================================================
  // Dates: ISO weeks, Easter, holidays
  // =====================================================================================

  function dateKey(d) { return d.getFullYear() + '-' + (d.getMonth() + 1) + '-' + d.getDate(); }
  function addDays(d, n) { return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n); }
  function today() { var n = new Date(); return new Date(n.getFullYear(), n.getMonth(), n.getDate()); }

  function isoWeek(d) {
    var t = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
    var day = t.getUTCDay() || 7;
    t.setUTCDate(t.getUTCDate() + 4 - day);
    var yearStart = new Date(Date.UTC(t.getUTCFullYear(), 0, 1));
    return Math.ceil(((t - yearStart) / 86400000 + 1) / 7);
  }

  function isoWeekMonday(year, week) {
    var jan4 = new Date(year, 0, 4);
    var offset = (jan4.getDay() + 6) % 7;
    return new Date(year, 0, 4 - offset + (week - 1) * 7);
  }

  /** Gregorian Easter Sunday (Meeus algorithm) */
  function easterSunday(year) {
    var a = year % 19, b = Math.floor(year / 100), c = year % 100, d = Math.floor(b / 4), e = b % 4;
    var f = Math.floor((b + 8) / 25), g = Math.floor((b - f + 1) / 3);
    var h = (19 * a + b - d - g + 15) % 30, i = Math.floor(c / 4), k = c % 4;
    var l = (32 + 2 * e + 2 * i - h - k) % 7, m = Math.floor((a + 11 * h + 22 * l) / 451);
    var month = Math.floor((h + l - 7 * m + 114) / 31), day = ((h + l - 7 * m + 114) % 31) + 1;
    return new Date(year, month - 1, day);
  }

  /** Holidays for one year: key -> tooltip. First rule wins if dates overlap. */
  function holidaysForYear(year) {
    var map = {};
    function add(d, text) { var k = dateKey(d); if (!(k in map)) map[k] = text; }

    add(new Date(year, 0, 1), tx(HOLIDAY_TEXTS.fixed[0]));
    add(new Date(year, 4, 1), tx(HOLIDAY_TEXTS.fixed[1]));
    add(new Date(year, 4, 17), tx(HOLIDAY_TEXTS.fixed[2]));
    add(new Date(year, 11, 31), tx(HOLIDAY_TEXTS.fixed[3]));

    for (var day = 23; day <= 30; day++) {
      var idx = day <= 26 ? day - 23 : 4;
      add(new Date(year, 11, day), tx(HOLIDAY_TEXTS.christmas[idx]));
    }

    var easter = easterSunday(year);
    var easterDays = [];
    if (settings.easterFullWeek) {
      var monday = addDays(easter, -((easter.getDay() + 6) % 7));
      for (var d = monday; d <= addDays(easter, 1); d = addDays(d, 1)) easterDays.push(d);
    } else {
      easterDays = [addDays(easter, -3), addDays(easter, -2), easter, addDays(easter, 1)];
    }
    easterDays.forEach(function (d) {
      var diff = Math.round((d - easter) / 86400000);
      var map2 = { '-3': 0, '-2': 1, '-1': 2, '0': 3, '1': 4 };
      add(d, tx(HOLIDAY_TEXTS.easter[diff in map2 ? map2[diff] : 5]));
    });

    add(addDays(easter, 39), tx(HOLIDAY_TEXTS.related[0]));
    add(addDays(easter, 49), tx(HOLIDAY_TEXTS.related[1]));
    add(addDays(easter, 50), tx(HOLIDAY_TEXTS.related[2]));

    var weeks = settings.summerWeeks;
    for (var w = 28; w <= 30; w++) {
      var include = weeks === 3 || (weeks === 2 && w >= 29) || (weeks === 1 && w === 30);
      if (!include) continue;
      var mon = isoWeekMonday(year, w);
      for (var j = 0; j < 7; j++) add(addDays(mon, j), tx(HOLIDAY_TEXTS.summer));
    }
    return map;
  }

  var holidays = {};
  function loadHolidays(years) {
    holidays = {};
    if (!settings.showHolidays) return;
    years.forEach(function (y) {
      var m = holidaysForYear(y);
      Object.keys(m).forEach(function (k) { if (!(k in holidays)) holidays[k] = m[k]; });
    });
  }

  // =====================================================================================
  // Text fitting (like WPF Viewbox: text grows/shrinks to fill its box)
  // =====================================================================================

  // Measured with a hidden span so it uses the same font the page renders with. (A canvas
  // ignores font stacks like "system-ui" on some browsers and then measures the wrong font,
  // so on iPhone the bottom-row text was not shrunk and got cut off.)
  var measureSpan = null;
  function fitSize(text, w, h, weight) {
    if (!measureSpan) {
      measureSpan = document.createElement('span');
      measureSpan.style.cssText = 'position:absolute;left:-9999px;top:0;visibility:hidden;white-space:nowrap;font-size:100px;';
      document.body.appendChild(measureSpan);
    }
    measureSpan.style.fontWeight = weight || 'normal';
    measureSpan.textContent = text;
    var width = Math.max(1, measureSpan.getBoundingClientRect().width) / 100;
    return Math.max(4, Math.min(w / width, h / 1.33));
  }

  // =====================================================================================
  // Drawing one month (8 x 8 grid: bar, day labels, 6 date rows)
  // =====================================================================================

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  function place(e, row, col, colSpan) {
    e.style.gridRow = String(row);
    e.style.gridColumn = col + (colSpan ? ' / span ' + colSpan : '');
    return e;
  }

  var ARROW_LEFT = '<svg viewBox="0 0 24 24"><path d="M20 12H5m6-7-7 7 7 7" fill="none" stroke="currentColor" stroke-width="2.6"/></svg>';
  var ARROW_RIGHT = '<svg viewBox="0 0 24 24"><path d="M4 12h15m-6-7 7 7-7 7" fill="none" stroke="currentColor" stroke-width="2.6"/></svg>';

  /**
   * kind: 'side'   = month + year (left/right month in the 3-month view)
   *       'middle' = arrows + clickable month + year (middle month)
   *       'year'   = month name only (year view)
   */
  function renderMonth(container, year, month0, kind, cellW, cellH) {
    container.innerHTML = '';
    var names = monthNames(), labels = dayNames();
    container.appendChild(el('div', 'bar'));

    var monthText, yearText;
    if (kind === 'year') {
      monthText = place(el('div', 'barText', names[month0]), 1, 2, 6);
      monthText.style.fontSize = fitSize(names[month0], cellW * 6 - 4, cellH - 2) + 'px';
      container.appendChild(monthText);
    } else {
      monthText = place(el('div', 'barText', names[month0]), 1, 3, 3);
      yearText = place(el('div', 'barText', String(year)), 1, 6, 2);
      monthText.style.fontSize = fitSize(names[month0], cellW * 3 - 4, cellH - 2) + 'px';
      yearText.style.fontSize = fitSize(String(year), cellW * 2 - 4, cellH - 4) + 'px';
      container.appendChild(monthText);
      container.appendChild(yearText);
      if (kind === 'middle') {
        monthText.classList.add('clickable');
        yearText.classList.add('clickable');
        monthText.addEventListener('click', function () { openMonthMenu(monthText); });
        yearText.addEventListener('click', function () { openYearMenu(yearText); });
        var back = place(el('button', 'nav'), 1, 1); back.innerHTML = ARROW_LEFT; back.title = T('Previous month', 'Forrige måned');
        var fwd = place(el('button', 'nav'), 1, 8); fwd.innerHTML = ARROW_RIGHT; fwd.title = T('Next month', 'Neste måned');
        back.addEventListener('click', function () { changeMonth(-1); });
        fwd.addEventListener('click', function () { changeMonth(1); });
        container.appendChild(back);
        container.appendChild(fwd);
      }
    }

    for (var x = 0; x < 8; x++) {
      var label = place(el('div', 'dayLabel', labels[x]), 2, x + 1);
      label.style.fontSize = fitSize(labels[x], cellW - 4 - (x === 0 ? 1.6 : 1), cellH - 4 - 1) + 'px';
      container.appendChild(label);
    }

    var first = new Date(year, month0, 1);
    var offset = (first.getDay() + 6) % 7;
    var daysInMonth = new Date(year, month0 + 1, 0).getDate();
    var t = today();
    var dateFont = Math.min(fitSize('28', cellW - 2 - 3.2, cellH - 2 - 3.2), (cellH - 2 - 3.2) / 1.33) + 'px';

    for (var day = 1; day <= daysInMonth; day++) {
      var cellIndex = offset + day - 1;
      var row = 3 + Math.floor(cellIndex / 7);
      var col = (cellIndex % 7) + 2;
      var date = new Date(year, month0, day);
      var key = dateKey(date);
      var cell = place(el('div', 'cell day', String(day)), row, col);
      cell.style.fontSize = dateFont;

      if (date.getTime() === t.getTime()) {
        cell.className = 'cell today';
        // 5 September keeps its gold on the day itself, with today's red border around it
        if (month0 === 8 && day === 5) {
          cell.classList.add('gold');
          cell.dataset.tip = tx(HOLIDAY_TEXTS.birthday);
        }
      } else if (key in holidays) {
        cell.classList.add('holiday');
        cell.dataset.tip = holidays[key];
      } else if (month0 === 8 && day === 5) {
        cell.classList.add('gold');
        cell.dataset.tip = tx(HOLIDAY_TEXTS.birthday);
      }
      container.appendChild(cell);

      if (col === 2 || day === 1) {
        var wk = place(el('div', 'cell week', String(isoWeek(date))), row, 1);
        wk.style.fontSize = dateFont;
        container.appendChild(wk);
      }
    }
  }

  // =====================================================================================
  // Views and navigation
  // =====================================================================================

  var $ = function (id) { return document.getElementById(id); };
  var pages = ['monthView', 'yearView', 'settingsPage', 'installPage', 'autostartPage', 'welcomePage', 'installedPage'];
  var currentPage = 'monthView';
  var activeMonth = new Date(today().getFullYear(), today().getMonth(), 1);
  var activeYear = today().getFullYear();
  var subPageReturn = 'settingsPage';
  var welcomeFromSettings = false;

  function isMaximized() {
    if (PIP) return false;
    return window.outerWidth >= screen.availWidth - 16 && window.outerHeight >= screen.availHeight - 16;
  }
  function isCalendarPage(id) { return id === 'monthView' || id === 'yearView'; }

  // Year view: 4 x 3 months on a PC screen, 3 x 4 on phones and tablets (they are held upright).
  // Same month size either way, so the page just gets narrower and taller.
  function yearLayout() {
    return touchOnly() ? { cols: 3, rows: 4, w: 768, h: 34 + 20 + 4 * (680 - 34 - 20) / 3 }
                       : { cols: 4, rows: 3, w: 1024, h: 680 };
  }

  function showPage(id) {
    closePopups();
    if (id === 'yearView') {
      var L = yearLayout();
      $(id).dataset.w = L.w;
      $(id).dataset.h = L.h;
    }
    pages.forEach(function (p) { $(p).classList.toggle('hidden', p !== id); });
    currentPage = id;
    var page = $(id), scaler = $('scaler');
    scaler.style.width = page.dataset.w + 'px';
    scaler.style.height = page.dataset.h + 'px';
    rescale();
    fitInfoTexts();
  }

  // Shrink the bottom-left hint to fit its space. On the calendar page the hint may also use the free part
  // of the middle column, left of the Settings button. Measured on the shown page, and on phones with
  // some room to spare: iPhone draws small text in the scaled page a little wider than it measures.
  function fitInfoTexts() {
    var spare = touchOnly() ? 0.9 : 1;
    [$('infoText'), $('yearInfoText')].forEach(function (e) {
      var box = e.parentElement, page = $(e.closest('.page').id), colWidth = (+page.dataset.w - 16) / 3;
      var width = colWidth;
      if (box.classList.contains('wide')) {
        var buttons = 0;   // Settings, and "Always on top" when it is shown
        Array.prototype.forEach.call(box.parentElement.querySelectorAll('.linkBox'), function (b) { if (b.offsetWidth) buttons += b.offsetWidth + 4; });
        width = colWidth * 2 + 8 - (buttons || 79) - 6;
      }
      var keys = e.querySelectorAll('.spaceKey').length * 3 + e.querySelectorAll('.arrowKey').length * 1.4;
      var text = e.textContent + new Array(Math.ceil(keys) + 1).join('\u2003');   // room for the keys
      var size = Math.min(10.5, fitSize(text, width - 10, 18) * spare);
      e.style.fontSize = size + 'px';
      if (!e.offsetParent) return;
      var avail = width * spare, w = e.offsetWidth;
      if (w > avail) e.style.fontSize = size * avail / w + 'px';
    });
  }

  function rescale() {
    var page = $(currentPage), scaler = $('scaler');
    var w = +page.dataset.w, h = +page.dataset.h;
    var s = Math.min(window.innerWidth / w, window.innerHeight / h);
    // Text pages in a maximized window: natural size, centered (not blown up)
    if (!isCalendarPage(currentPage) && isMaximized()) s = Math.min(s, 1);
    scaler.style.transform = 'scale(' + s + ')';
    scaler.style.left = Math.max(0, (window.innerWidth - w * s) / 2) + 'px';
    scaler.style.top = Math.max(0, (window.innerHeight - h * s) / 2) + 'px';
  }

  function showCalendar() {
    if (isMaximized()) { showPage('yearView'); renderYearView(); }
    else { showPage('monthView'); renderMonthView(); }
  }

  function resetToToday() {
    var t = today();
    activeMonth = new Date(t.getFullYear(), t.getMonth(), 1);
    activeYear = t.getFullYear();
    showCalendar();
  }

  function renderMonthView() {
    var prev = new Date(activeMonth.getFullYear(), activeMonth.getMonth() - 1, 1);
    var next = new Date(activeMonth.getFullYear(), activeMonth.getMonth() + 1, 1);
    loadHolidays(uniq([prev.getFullYear(), activeMonth.getFullYear(), next.getFullYear()]));
    var cw = (600 - 16) / 3 / 8, ch = 200 / 8;
    renderMonth($('m1'), prev.getFullYear(), prev.getMonth(), 'side', cw, ch);
    renderMonth($('m2'), activeMonth.getFullYear(), activeMonth.getMonth(), 'middle', cw, ch);
    renderMonth($('m3'), next.getFullYear(), next.getMonth(), 'side', cw, ch);
  }

  function renderYearView() {
    $('yearTitle').textContent = String(activeYear);
    loadHolidays([activeYear]);
    var grid = $('yearGrid'), L = yearLayout();
    grid.innerHTML = '';
    grid.style.gridTemplateColumns = 'repeat(' + L.cols + ', 1fr)';
    grid.style.gridTemplateRows = 'repeat(' + L.rows + ', 1fr)';
    var cw = (L.w / L.cols - 8) / 8, ch = ((L.h - 34 - 20) / L.rows - 8) / 8;
    for (var m = 0; m < 12; m++) {
      var box = el('div', 'month');
      grid.appendChild(box);
      renderMonth(box, activeYear, m, 'year', cw, ch);
    }
  }

  function uniq(a) { return a.filter(function (v, i) { return a.indexOf(v) === i; }); }

  function changeMonth(n) {
    activeMonth = new Date(activeMonth.getFullYear(), activeMonth.getMonth() + n, 1);
    renderMonthView();
  }
  function changeYear(n) {
    if (currentPage === 'yearView') { activeYear += n; renderYearView(); }
    else { activeMonth = new Date(activeMonth.getFullYear() + n, activeMonth.getMonth(), 1); renderMonthView(); }
  }

  // ---------- Month / year pickers ----------
  function showMenu(anchor, items) {
    var menu = $('menu');
    menu.innerHTML = '';
    items.forEach(function (it) {
      var row = el('div', it.current ? 'current' : '', it.label);
      row.addEventListener('click', function (e) { e.stopPropagation(); closePopups(); it.action(); });
      menu.appendChild(row);
    });
    menu.classList.remove('hidden');
    var r = anchor.getBoundingClientRect();
    var top = r.bottom + 2, left = r.left;
    var mh = menu.offsetHeight, mw = menu.offsetWidth;
    if (top + mh > window.innerHeight) top = Math.max(0, window.innerHeight - mh - 2);
    if (left + mw > window.innerWidth) left = Math.max(0, window.innerWidth - mw - 2);
    menu.style.top = top + 'px';
    menu.style.left = left + 'px';
  }
  function openMonthMenu(anchor) {
    var names = monthNames();
    showMenu(anchor, names.map(function (n, i) {
      return { label: n, current: i === activeMonth.getMonth(), action: function () { activeMonth = new Date(activeMonth.getFullYear(), i, 1); renderMonthView(); } };
    }));
  }
  function openYearMenu(anchor) {
    var y0 = activeMonth.getFullYear(), items = [];
    for (var y = y0 - 3; y <= y0 + 3; y++) {
      (function (year) {
        items.push({ label: String(year), current: year === y0, action: function () { activeMonth = new Date(year, activeMonth.getMonth(), 1); renderMonthView(); } });
      })(y);
    }
    showMenu(anchor, items);
  }

  // ---------- Holiday tooltips ----------
  var tipTimer = null;
  function showTip(cell) {
    var tip = $('tooltip');
    tip.textContent = cell.dataset.tip;
    tip.classList.remove('hidden');
    var r = cell.getBoundingClientRect();
    var top = r.bottom + 4, left = r.left;
    if (top + tip.offsetHeight > window.innerHeight) top = r.top - tip.offsetHeight - 4;
    if (left + tip.offsetWidth > window.innerWidth) left = window.innerWidth - tip.offsetWidth - 4;
    tip.style.top = top + 'px';
    tip.style.left = Math.max(0, left) + 'px';
  }
  function hideTip() { clearTimeout(tipTimer); $('tooltip').classList.add('hidden'); }
  function closePopups() { $('menu').classList.add('hidden'); hideTip(); }

  var scalerEl = document.getElementById('scaler');
  scalerEl.addEventListener('mouseover', function (e) {
    var cell = e.target.closest && e.target.closest('.cell[data-tip]');
    if (!cell) return;
    clearTimeout(tipTimer);
    tipTimer = setTimeout(function () { showTip(cell); }, 400);
  });
  scalerEl.addEventListener('mouseout', function (e) {
    var cell = e.target.closest && e.target.closest('.cell[data-tip]');
    if (cell && !cell.contains(e.relatedTarget)) hideTip();
  });
  scalerEl.addEventListener('click', function (e) {
    var cell = e.target.closest && e.target.closest('.cell[data-tip]');
    if (cell) { clearTimeout(tipTimer); showTip(cell); }
  });
  document.addEventListener('click', function (e) {
    if (!$('menu').contains(e.target) && !(e.target.closest && e.target.closest('.clickable'))) $('menu').classList.add('hidden');
  });

  // ---------- Keyboard: ← → month (year in year view), ↑ ↓ year, Space = today ----------
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') { closePopups(); return; }
    if (e.repeat || !isCalendarPage(currentPage)) return;
    var tag = (e.target && e.target.tagName) || '';
    if (tag === 'INPUT' || tag === 'SELECT') return;
    var yearView = currentPage === 'yearView';
    switch (e.key) {
      case 'ArrowRight': yearView ? changeYear(1) : changeMonth(1); break;
      case 'ArrowLeft': yearView ? changeYear(-1) : changeMonth(-1); break;
      case 'ArrowUp': changeYear(1); break;
      case 'ArrowDown': changeYear(-1); break;
      case ' ': case 'Spacebar': resetToToday(); break;
      default: return;
    }
    closePopups();
    e.preventDefault();
  });

  // ---------- Touch swipe: same as the keys. Left/right = ← →, down = Space (today) ----------
  var swipe = null;
  document.addEventListener('pointerdown', function (e) {
    swipe = (e.pointerType === 'touch' || e.pointerType === 'pen') && e.isPrimary && isCalendarPage(currentPage)
      ? { x: e.clientX, y: e.clientY, t: Date.now() } : null;
  });
  document.addEventListener('pointercancel', function () { swipe = null; });
  document.addEventListener('pointerup', function (e) {
    if (!swipe || !e.isPrimary) return;
    var dx = e.clientX - swipe.x, dy = e.clientY - swipe.y, quick = Date.now() - swipe.t < 1000;
    swipe = null;
    if (!quick || !isCalendarPage(currentPage)) return;
    var yearView = currentPage === 'yearView';
    if (Math.abs(dx) >= 40 && Math.abs(dx) > Math.abs(dy) * 1.5) {
      var step = dx < 0 ? 1 : -1;   // swipe left = forward, like turning a page
      if (yearView) changeYear(step); else changeMonth(step);
    } else if (dy >= 40 && dy > Math.abs(dx) * 1.5) {
      resetToToday();
    } else return;
    closePopups();
  });

  $('yearBack').addEventListener('click', function () { changeYear(-1); });
  $('yearForward').addEventListener('click', function () { changeYear(1); });

  // ---------- Window behaviour ----------
  var wasMaximized = isMaximized();
  var appSizeReady = false, saveSizeTimer = null;
  window.addEventListener('resize', function () {
    closePopups();
    var max = isMaximized();
    // Installed app: remember the size, like the Windows version does
    // (not while "Always on top" has shrunk this window, see pip.js)
    var floating = function () { return document.documentElement.classList.contains('pipActive'); };
    if (appSizeReady && isInstalled() && !floating()) {
      clearTimeout(saveSizeTimer);
      saveSizeTimer = setTimeout(function () {
        if (floating()) return;
        settings.appMaximized = isMaximized();
        if (!settings.appMaximized) settings.appSize = { w: window.innerWidth, h: window.innerHeight };
        saveSettings();
      }, 400);
    }
    if (max !== wasMaximized) {
      wasMaximized = max;
      // Maximize = whole year, back to normal = 3 months with the current month in the middle
      if (isCalendarPage(currentPage)) { resetToToday(); return; }
    }
    rescale();
  });

  // "Minimize app to reset date" (installed app only)
  var wasHidden = false;
  document.addEventListener('visibilitychange', function () {
    if (document.hidden) { wasHidden = true; return; }
    // Only in the installed app: in a browser, switching tabs should not move the calendar
    if (wasHidden && isInstalled() && isCalendarPage(currentPage)) resetToToday();
    wasHidden = false;
    updateBadge(true);
  });

  // =====================================================================================
  // Week number badge on the taskbar icon (installed app)
  // =====================================================================================

  var shownWeek = -1, shownDay = dateKey(today());
  function updateBadge(force) {
    var t = today();
    var week = isoWeek(t);
    if (force || week !== shownWeek) {
      shownWeek = week;
      if ('setAppBadge' in navigator) {
        try { navigator.setAppBadge(week).catch(function () {}); } catch (e) { /* not supported */ }
      }
      document.title = 'QuickCal – ' + T('week ', 'uke ') + week;
      $('wWeekIcon').textContent = String(week);
    }
    var k = dateKey(t);
    if (k !== shownDay) {
      shownDay = k;
      if (currentPage === 'monthView') renderMonthView();
      else if (currentPage === 'yearView') renderYearView();
    }
  }
  setInterval(function () { updateBadge(false); }, 30000);
  window.addEventListener('focus', function () { updateBadge(false); });
  window.addEventListener('pageshow', function () { updateBadge(true); });

  // =====================================================================================
  // Install as app
  // =====================================================================================

  var installPrompt = null;
  function isInstalled() {
    return (window.matchMedia && (matchMedia('(display-mode: standalone)').matches || matchMedia('(display-mode: window-controls-overlay)').matches)) ||
      window.navigator.standalone === true;
  }
  function browserKind() {
    var ua = navigator.userAgent || '';
    var brands = (navigator.userAgentData && navigator.userAgentData.brands) || [];
    if (/Edg\//.test(ua) || brands.some(function (b) { return /Edge/i.test(b.brand); })) return 'edge';
    if (/Chrome\//.test(ua) || brands.some(function (b) { return /Chrome/i.test(b.brand); })) return 'chrome';
    return 'other';
  }

  // Phone or tablet: installing means "add to the home screen", and there is no taskbar or autostart.
  // iPadOS reports itself as a Mac, so a Mac with a touch screen counts as an iPad.
  function mobileOS() {
    var ua = navigator.userAgent || '';
    if (/iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return 'ios';
    if (/Android/.test(ua)) return 'android';
    return null;
  }

  window.addEventListener('beforeinstallprompt', function (e) {
    e.preventDefault();
    installPrompt = e;
    fillHelpPages();
  });
  window.addEventListener('appinstalled', function () {
    installPrompt = null;
    fillHelpPages();
  });

  function runInstall() {
    if (!installPrompt) { openSubPage('installPage'); return; }
    installPrompt.prompt();
    installPrompt.userChoice.then(function (choice) {
      if (choice && choice.outcome === 'accepted') installPrompt = null;
      fillHelpPages();
    });
  }

  function updateInstallButtons() {
    var installed = isInstalled();
    var w = $('wInstallBtn'), now = $('installNowBtn');
    if (installed) {
      w.textContent = T('Installed ✓', 'Installert ✓');
      w.disabled = true;
    } else {
      w.disabled = false;
      w.textContent = installPrompt ? T('Install', 'Installer') : T('Show me how', 'Vis meg hvordan');
    }
    now.classList.toggle('hidden', !installPrompt || installed);
    $('resetSizeBtn').classList.toggle('hidden', !installed);
    var mobile = mobileOS();
    $('installPageBtn').textContent = mobile
      ? (installed ? T('Remove app', 'Fjern appen') : T('Add to home screen', 'Legg på Hjem-skjermen'))
      : installed ? T('Uninstall', 'Avinstaller') : T('Install as app', 'Installer som app');
    // Phones and tablets have no taskbar, autostart or window size
    $('autostartPageBtn').classList.toggle('hidden', !!mobile);
    if (mobile) $('resetSizeBtn').classList.add('hidden');
    now.textContent = T('Install QuickCal now', 'Installer QuickCal nå');
  }

  // =====================================================================================
  // Pages: settings, install, autostart, getting started
  // =====================================================================================

  function openSubPage(id) {
    subPageReturn = currentPage === 'welcomePage' || currentPage === 'installedPage' ? currentPage : 'settingsPage';
    fillHelpPages();
    showPage(id);
  }
  Array.prototype.forEach.call(document.querySelectorAll('.subBack'), function (b) {
    b.addEventListener('click', function () {
      if (subPageReturn === 'calendar') showCalendar(); else showPage(subPageReturn);
    });
  });

  function setCheck(btn, on) {
    btn.classList.toggle('on', on);
    btn.textContent = touchOnly() ? '' : on ? '🗹' : '☐';   // drawn as a switch by CSS on touch devices
    btn.dataset.on = on ? '1' : '';
    btn.setAttribute('role', 'switch');
    btn.setAttribute('aria-checked', on ? 'true' : 'false');
  }
  function isChecked(btn) { return btn.dataset.on === '1'; }
  function updateHolidayRows() {
    var on = isChecked($('holidaysCheck'));
    Array.prototype.forEach.call(document.querySelectorAll('.dependsOnHolidays'), function (r) { r.classList.toggle('hidden', !on); });
  }

  function openSettings() {
    $('languageSelect').value = settings.language;
    setCheck($('holidaysCheck'), settings.showHolidays);
    setCheck($('easterCheck'), settings.easterFullWeek);
    $('weeksInput').value = String(settings.summerWeeks);
    updateHolidayRows();
    showPage('settingsPage');
  }
  // Language takes effect at once, so the settings page itself switches language
  $('languageSelect').addEventListener('change', function () {
    settings.language = this.value;
    saveSettings();
    applyTexts();
    updateBadge(true);
  });
  $('settingsBtn1').addEventListener('click', openSettings);
  $('settingsBtn2').addEventListener('click', openSettings);
  $('holidaysCheck').addEventListener('click', function () { setCheck(this, !isChecked(this)); updateHolidayRows(); });
  $('easterCheck').addEventListener('click', function () { setCheck(this, !isChecked(this)); });
  $('weeksInput').addEventListener('input', function () { this.value = this.value.replace(/[^0-3]/g, '').slice(0, 1); });

  $('settingsBack').addEventListener('click', function () {
    settings.language = $('languageSelect').value;
    settings.showHolidays = isChecked($('holidaysCheck'));
    settings.easterFullWeek = isChecked($('easterCheck'));
    var w = parseInt($('weeksInput').value, 10);
    if (!isNaN(w)) settings.summerWeeks = Math.max(0, Math.min(3, w));
    saveSettings();
    applyTexts();
    updateBadge(true);
    showCalendar();
  });
  $('installPageBtn').addEventListener('click', function () { openSubPage('installPage'); });
  $('autostartPageBtn').addEventListener('click', function () { openSubPage('autostartPage'); });
  $('installNowBtn').addEventListener('click', runInstall);
  $('copyAppsBtn').addEventListener('click', function () {
    var btn = this, url = browserKind() === 'chrome' ? 'chrome://apps' : 'edge://apps';
    function done() { btn.textContent = T('Copied ✓ Now paste it in the address bar', 'Kopiert ✓ Lim den inn i adressefeltet'); }
    try {
      navigator.clipboard.writeText(url).then(done, function () { prompt(T('Copy this address:', 'Kopier denne adressen:'), url); });
    } catch (e) { prompt(T('Copy this address:', 'Kopier denne adressen:'), url); }
  });

  function showWelcome() {
    welcomeFromSettings = currentPage === 'settingsPage';
    updateInstallButtons();
    showPage('welcomePage');
  }
  $('welcomeBtn').addEventListener('click', showWelcome);
  $('wInstallBtn').addEventListener('click', runInstall);
  $('wAutostartBtn').addEventListener('click', function () { openSubPage('autostartPage'); });
  $('welcomeOk').addEventListener('click', function () {
    if (welcomeFromSettings) showPage('settingsPage'); else showCalendar();
  });

  // Shown once, on the first start as an installed app. Edge asks at that moment whether the app may
  // create a desktop shortcut and start when you sign in, so explain both and recommend Allow.
  function showInstalledPage() {
    if (mobileOS()) { settings.appAutostartShown = true; saveSettings(); return; }
    var edge = browserKind() === 'edge';
    $('installedTitle').textContent = T('QuickCal is installed', 'QuickCal er installert');
    $('installedIntro').textContent = edge
      ? T('Edge now asks what QuickCal may do. Tick both and click Allow:',
          'Edge spør nå hva QuickCal skal få lov til. Kryss av for begge og trykk Tillat:')
      : T('Two things make QuickCal work best:', 'To ting gjør at QuickCal fungerer best:');
    var steps = [];
    if (edge) {
      steps.push(T('Create desktop shortcut: a QuickCal icon on the desktop.',
                   'Opprett skrivebordssnarvei: et QuickCal-ikon på skrivebordet.'));
    }
    steps.push(edge
      ? T('Start automatically on device login: QuickCal opens when you sign in, so the week number is always on the taskbar.',
          'Starter automatisk ved enhetspålogging: QuickCal åpnes når du logger på, så ukenummeret alltid står på oppgavelinjen.')
      : T('Start automatically when you sign in: see “Start automatically” in the settings.',
          'Start automatisk når du logger på: se «Start automatisk» under innstillinger.'));
    steps.push(T('Pin to taskbar: right-click the QuickCal icon on the taskbar and choose “Pin to taskbar”.',
                 'Fest til oppgavelinjen: høyreklikk QuickCal-ikonet på oppgavelinjen og velg «Fest til oppgavelinjen».'));
    setList('installedSteps', steps);
    $('installedTip').textContent = edge
      ? T('Clicked Allow too quickly? Change the choices at any time: ⋯ at the top right of this window → App settings ⚙.',
          'Trykket du Tillat for fort? Endre valgene når som helst: ⋯ øverst til høyre i dette vinduet → Appinnstillinger ⚙.')
      : '';
    subPageReturn = 'calendar';
    showPage('installedPage');
    settings.appAutostartShown = true;
    saveSettings();
  }
  $('installedOk').addEventListener('click', showCalendar);

  function setList(id, items) {
    var ol = $(id);
    ol.innerHTML = '';
    items.forEach(function (t) { ol.appendChild(el('li', '', t)); });
  }

  function fillHelpPages() {
    var b = browserKind();

    // Install page (becomes the uninstall page when QuickCal runs as an installed app)
    var pin = T('Right-click the QuickCal icon on the taskbar and choose “Pin to taskbar”. The week number is shown on the icon.',
      'Høyreklikk QuickCal-ikonet på oppgavelinjen og velg «Fest til oppgavelinjen». Ukenummeret vises på ikonet.');
    var mobile = mobileOS();
    if (mobile && isInstalled()) {
      $('installTitle').textContent = T('Remove QuickCal', 'Fjern QuickCal');
      $('installIntro').textContent = T(
        'A web app cannot remove itself, but it only takes a moment:',
        'En webapp kan ikke fjerne seg selv, men det tar bare et øyeblikk:');
      setList('installSteps', mobile === 'ios' ? [
        T('Touch and hold the QuickCal icon on the Home Screen.', 'Hold fingeren på QuickCal-ikonet på Hjem-skjermen.'),
        T('Choose “Remove App” or “Delete Bookmark” and confirm.', 'Velg «Fjern app» eller «Slett bokmerke» og bekreft.')] : [
        T('Touch and hold the QuickCal icon on the home screen.', 'Hold fingeren på QuickCal-ikonet på startskjermen.'),
        T('Choose “Uninstall” (or drag the icon to Uninstall) and confirm.', 'Velg «Avinstaller» (eller dra ikonet til Avinstaller) og bekreft.')]);
      $('installTip').textContent = T(
        'You can add QuickCal again at any time from the same web address.',
        'Du kan legge til QuickCal igjen når som helst fra den samme nettadressen.');
    } else if (mobile) {
      $('installTitle').textContent = T('Add QuickCal to the home screen', 'Legg QuickCal på Hjem-skjermen');
      $('installIntro').textContent = T(
        'As an app, QuickCal gets its own icon, opens in full screen and works offline.',
        'Som app får QuickCal sitt eget ikon, åpnes i fullskjerm og virker uten nett.');
      if (installPrompt) {
        setList('installSteps', [
          T('Tap “Install QuickCal now” below and confirm with Install.',
            'Trykk «Installer QuickCal nå» under og bekreft med Installer.')]);
      } else if (mobile === 'ios') {
        setList('installSteps', [
          T('Tap Share ⬆ (in Safari at the bottom or next to the address bar, in Chrome at the top right).',
            'Trykk Del ⬆ (i Safari nederst eller ved adressefeltet, i Chrome øverst til høyre).'),
          T('Choose “Add to Home Screen”. Scroll down in the list if you do not see it.',
            'Velg «Legg til på Hjem-skjerm». Bla ned i listen hvis du ikke ser valget.'),
          T('Leave “Open as Web App” on if it is shown, and tap Add.',
            'La «Åpne som webapp» være på hvis valget vises, og trykk Legg til.')]);
      } else {
        setList('installSteps', [
          T('Open the browser menu ⋮ at the top right.', 'Åpne nettlesermenyen ⋮ øverst til høyre.'),
          T('Choose “Install app” or “Add to Home screen” and confirm.',
            'Velg «Installer app» eller «Legg til på startsiden» og bekreft.')]);
      }
      $('installTip').textContent = mobile === 'ios'
        ? T('Works in Safari and Chrome. QuickCal goes to today every time you open it again.',
            'Virker i Safari og Chrome. QuickCal går til i dag hver gang du åpner den igjen.')
        : T('QuickCal goes to today every time you open it again.', 'QuickCal går til i dag hver gang du åpner den igjen.');
    } else if (isInstalled()) {
      $('installTitle').textContent = T('Uninstall QuickCal', 'Avinstaller QuickCal');
      $('installIntro').textContent = T(
        'A web app cannot uninstall itself, but it only takes a moment:',
        'En webapp kan ikke avinstallere seg selv, men det tar bare et øyeblikk:');
      if (b === 'edge') {
        setList('installSteps', [appMenuStep(),
          T('Choose “App settings” ⚙, then “Uninstall”.', 'Velg «Appinnstillinger» ⚙ og deretter «Avinstaller».')]);
      } else setList('installSteps', [
        T('Open the Windows Start menu, find QuickCal, right-click it and choose Uninstall.',
          'Åpne Start-menyen i Windows, finn QuickCal, høyreklikk og velg Avinstaller.'),
        b === 'chrome'
          ? T('Or: go to chrome://apps, right-click QuickCal and choose Uninstall (Remove from Chrome).',
              'Eller: gå til chrome://apps, høyreklikk QuickCal og velg Avinstaller (Fjern fra Chrome).')
          : T('Or: go to edge://apps, click … next to QuickCal and choose Uninstall.',
              'Eller: gå til edge://apps, klikk … ved QuickCal og velg Avinstaller.')]);
      $('installTip').textContent = T(
        'You can install QuickCal again at any time from the same web address.',
        'Du kan installere QuickCal igjen når som helst fra den samme nettadressen.');
    } else {
      $('installTitle').textContent = T('Install QuickCal as an app', 'Installer QuickCal som app');
      $('installIntro').textContent = T(
        'As an app, QuickCal gets its own window, works offline and shows the week number on its taskbar icon.',
        'Som app får QuickCal sitt eget vindu, virker uten nett og viser ukenummeret på ikonet på oppgavelinjen.');
      if (installPrompt) {
        // The browser offers installation: the button below is all that is needed
        setList('installSteps', [
          T('Click “Install QuickCal now” below and confirm with Install. QuickCal opens in its own window.',
            'Klikk «Installer QuickCal nå» under og bekreft med Installer. QuickCal åpnes i sitt eget vindu.'),
          pin]);
      } else if (b === 'edge' || b === 'chrome') {
        setList('installSteps', [
          T('Click the install icon (a small monitor with an arrow) at the right end of the address bar.',
            'Klikk installeringsikonet (en liten skjerm med en pil) helt til høyre i adressefeltet.'),
          b === 'edge'
            ? T('No icon? Open the … menu → Apps → Install this site as an app.',
                'Finnes det ikke? Åpne menyen … → Apper → Installer dette nettstedet som en app.')
            : T('No icon? Open the ⋮ menu and look for “Install QuickCal…” (in some versions under Cast, save, and share).',
                'Finnes det ikke? Åpne menyen ⋮ og se etter «Installer QuickCal …» (i noen versjoner under Kringkast, lagre og del).'),
          pin]);
      } else {
        setList('installSteps', [
          T('Installing as an app works best in Microsoft Edge or Google Chrome. Open this page in one of them.',
            'Installering som app fungerer best i Microsoft Edge eller Google Chrome. Åpne denne siden i en av dem.')]);
      }
      $('installTip').textContent = T(
        'The week number is shown on the icon while QuickCal is open, so minimize the window instead of closing it, or turn on automatic start.',
        'Ukenummeret vises på ikonet så lenge QuickCal er åpen. Minimer derfor vinduet i stedet for å lukke det, eller slå på automatisk start.');
    }
    updateInstallButtons();

    // Autostart page
    $('autostartTitle').textContent = T('Start QuickCal when you sign in', 'Start QuickCal automatisk når du logger på');
    $('autostartIntro').textContent = isInstalled()
      ? T('Then the week number on the taskbar icon is always up to date, also after a restart. Browsers do not let a web app turn this on itself, but it only takes a moment:',
          'Da er ukenummeret på oppgavelinjen alltid oppdatert, også etter omstart. Nettleseren lar ikke en webapp slå dette på selv, men det tar bare et øyeblikk:')
      : T('Then the week number on the taskbar is always up to date. QuickCal must be installed as an app first.',
          'Da er ukenummeret på oppgavelinjen alltid oppdatert. QuickCal må være installert som app først.');
    var appsUrl = b === 'chrome' ? 'chrome://apps' : 'edge://apps';
    $('copyAppsBtn').textContent = T('Copy ', 'Kopier ') + appsUrl;
    // In the installed Edge app the ⋯ menu in the title bar leads straight to the app's settings
    var viaAppMenu = isInstalled() && b === 'edge';
    $('copyAppsBtn').classList.toggle('hidden', b === 'other' || viaAppMenu);
    if (viaAppMenu) {
      $('autostartIntro').textContent = T('Then the week number on the taskbar is always up to date, also after a restart.',
        'Da er ukenummeret på oppgavelinjen alltid oppdatert, også etter omstart.');
      setList('autostartSteps', [appMenuStep(),
        T('Choose “App settings” ⚙.', 'Velg «Appinnstillinger» ⚙.'),
        T('Turn on “Start automatically on device login”.', 'Slå på «Starter automatisk ved enhetspålogging».')]);
    } else if (b === 'chrome') {
      setList('autostartSteps', [
        T('Click the button below, then paste (Ctrl+V) into the address bar of a Chrome window and press Enter.',
          'Klikk knappen under, lim inn (Ctrl+V) i adressefeltet i et Chrome-vindu og trykk Enter.'),
        T('Right-click QuickCal and tick “Start app when you sign in”.',
          'Høyreklikk QuickCal og huk av for «Start appen når du logger på» (Start app when you sign in).')]);
    } else {
      setList('autostartSteps', [
        T('Click the button below, then paste (Ctrl+V) into the address bar of an Edge window and press Enter.',
          'Klikk knappen under, lim inn (Ctrl+V) i adressefeltet i et Edge-vindu og trykk Enter.'),
        T('Click … next to QuickCal and turn on “Auto-start on device login”.',
          'Klikk … ved QuickCal og slå på automatisk start ved pålogging (Auto-start on device login).')]);
    }
    $('autostartTip').textContent = viaAppMenu
      ? T('There you can also create a desktop shortcut or uninstall QuickCal.',
          'Der kan du også lage skrivebordssnarvei eller avinstallere QuickCal.')
      : T('The menu names may differ slightly between browser versions and languages.',
          'Navnene i menyene kan variere litt mellom nettleserversjoner og språk.');
  }

  function appMenuStep() {
    return T('Click ⋯ at the top right of the QuickCal window, next to – ☐ ✕.',
             'Klikk ⋯ øverst til høyre i QuickCal-vinduet, ved siden av – ☐ ✕.');
  }

  // Touch-only device (phone/tablet without mouse or trackpad): show swipe hints instead of
  // keyboard hints. A PC, also one with a touch screen, has a fine pointer and keeps the keyboard text.
  var touchQuery = window.matchMedia ? matchMedia('(hover: none) and (pointer: coarse)') : null;
  var finePointerQuery = window.matchMedia ? matchMedia('(any-pointer: fine)') : null;
  function touchOnly() {
    return !!touchQuery && touchQuery.matches && !finePointerQuery.matches;
  }
  [touchQuery, finePointerQuery].forEach(function (q) {
    if (!q) return;
    if (q.addEventListener) q.addEventListener('change', function () { applyTexts(); });
    else if (q.addListener) q.addListener(function () { applyTexts(); });
  });

  // Bottom-left hint with drawn keys: [←][→] leftRight [↑][↓] upDown [space] space.
  // upDown == null leaves out the [↑][↓] part (year view).
  function setKeyHint(e, leftRight, upDown, space) {
    e.textContent = '';
    e.appendChild(el('span', 'arrowKey', '\u2190'));
    e.appendChild(el('span', 'arrowKey', '\u2192'));
    e.appendChild(document.createTextNode(leftRight));
    if (upDown != null) {
      e.appendChild(el('span', 'arrowKey', '\u2191'));
      e.appendChild(el('span', 'arrowKey', '\u2193'));
      e.appendChild(document.createTextNode(upDown));
    }
    e.appendChild(el('span', 'spaceKey'));
    e.appendChild(document.createTextNode(space));
  }

  function applyTexts() {
    document.documentElement.classList.toggle('touch', touchOnly());
    if (currentPage === 'yearView' && +$('yearView').dataset.w !== yearLayout().w) { showPage('yearView'); renderYearView(); }
    [$('holidaysCheck'), $('easterCheck')].forEach(function (b) { setCheck(b, isChecked(b)); });
    document.documentElement.lang = norwegian() ? 'no' : 'en';
    if (touchOnly()) {
      $('infoText').textContent = T('Swipe ← → for month, ↓ for today', 'Sveip ← → for ny måned, ↓ for i dag');
      $('yearInfoText').textContent = T('Swipe ← → to change year, ↓ for this year', 'Sveip ← → for å bytte år, ↓ for i år');
    } else {
      // Minimizing only resets the date in the installed app (see visibilitychange), not in a browser tab
      setKeyHint($('infoText'), T(' month · ', ' måned · '), T(' year · ', ' år · '), isInstalled()
        ? T(' / minimize: today', ' / minimer: i dag')
        : T(' today', ' i dag'));
      setKeyHint($('yearInfoText'), T(' change year · ', ' bytt år · '), null, T(' this year', ' nåværende år'));
    }
    Array.prototype.forEach.call(document.querySelectorAll('.t-settings'), function (s) { s.textContent = T('Settings', 'Innstillinger'); });
    // Shrink the bottom-row texts if they are too long for their column (like the Viewbox in the Windows app)
    var colWidth = (600 - 16) / 3;
    fitInfoTexts();
    Array.prototype.forEach.call(document.querySelectorAll('.tagline'), function (e) {
      e.style.fontSize = Math.min(12.5, fitSize(e.textContent, colWidth - 6, 18)) + 'px';
    });

    $('settingsTitle').textContent = T('Settings', 'Innstillinger');
    $('welcomeBtn').textContent = T('Getting started', 'Kom i gang');
    $('languageLabel').textContent = T('Calendar Language', 'Kalenderspråk');
    $('systemLanguageOption').textContent = T('Use system language', 'Bruk systemspråk');
    $('holidaysLabel').textContent = T('Show holidays in calendar', 'Vis helligdager i kalenderen');
    $('easterLabel').textContent = T('Show Easter as a full week off', 'Vis hele påskeuka som fri');
    $('weeksBefore').textContent = T('Show', 'Vis');
    $('weeksAfter').textContent = T('number of weeks summer vacation (0-3)', 'uker sommerferie (0–3)');
    $('autostartPageBtn').textContent = T('Start automatically', 'Start automatisk');
    $('resetSizeBtn').textContent = T('Default window size', 'Standard vindusstørrelse');

    $('welcomeTitle').textContent = T('Getting started with QuickCal', 'Kom i gang med QuickCal');
    $('wInstallText').textContent = T('Install QuickCal as an app', 'Installer QuickCal som app');
    $('wBadgeText').innerHTML = '';
    $('wBadgeText').appendChild(document.createTextNode(T('Week number on the taskbar', 'Ukenummeret på oppgavelinjen')));
    $('wBadgeText').appendChild(document.createElement('br'));
    $('wBadgeText').appendChild(el('small', '', T(
      'When QuickCal is installed and open (minimized is fine), the week number is shown on its icon. Right-click the icon and choose “Pin to taskbar”.',
      'Når QuickCal er installert og åpen (gjerne minimert), vises ukenummeret på ikonet. Høyreklikk ikonet og velg «Fest til oppgavelinjen».')));
    $('wAutostartText').textContent = T('Start automatically when you sign in', 'Start automatisk når du logger på');
    // Phones and tablets: "add to the home screen", and no taskbar or autostart rows
    var mobile = mobileOS();
    if (mobile) $('wInstallText').textContent = T('Add QuickCal to the home screen', 'Legg QuickCal på Hjem-skjermen');
    $('wBadgeText').parentNode.classList.toggle('hidden', !!mobile);
    $('wAutostartText').parentNode.classList.toggle('hidden', !!mobile);
    $('wAutostartBtn').textContent = T('Show me how', 'Vis meg hvordan');
    $('wTip').textContent = touchOnly() ? T(
      'Tip: the arrows, the month name and the year at the top can be tapped. Swipe left or right to change month, and swipe down to go to today.',
      'Tips: Pilene, månedsnavnet og årstallet øverst kan trykkes på. Sveip til venstre eller høyre for å bytte måned, og ned for å gå til i dag.') : T(
      'Tip: the arrows, the month name and the year at the top are clickable. ← → change month, ↑ ↓ change year, and Spacebar goes to today. Maximize the window to see the whole year.',
      'Tips: Pilene, månedsnavnet og årstallet øverst er klikkbare. ← → bytter måned, ↑ ↓ bytter år, og mellomrom går til i dag. Maksimer vinduet for å se hele året.');
    updateInstallButtons();
    fillHelpPages();
  }

  // =====================================================================================
  // Start
  // =====================================================================================

  if ('serviceWorker' in navigator && location.protocol !== 'file:') {
    navigator.serviceWorker.register('sw.js').catch(function () { /* offline support is optional */ });
  }

  // Installed app: open with the same size as the Windows version (600 × 220), or the size
  // the user left it at. Browsers open new app windows very large, so this is done on every start.
  var DEFAULT_APP_SIZE = { w: 600, h: 220 };
  function sizeAppWindow(size) {
    if (!isInstalled()) return;
    size = size || settings.appSize || DEFAULT_APP_SIZE;
    var w = Math.max(300, Math.min(size.w, screen.availWidth)), h = Math.max(110, Math.min(size.h, screen.availHeight));
    try {
      window.resizeTo(w + (window.outerWidth - window.innerWidth), h + (window.outerHeight - window.innerHeight));
    } catch (e) { /* not allowed in this browser */ }
  }
  // The browser may still be placing the new window, so try a few times before remembering the size
  function startAppSizing() {
    appSizeReady = false;
    if (settings.appMaximized) { appSizeReady = true; return; }
    var tries = [0, 250, 800, 1600];
    tries.forEach(function (ms, i) {
      setTimeout(function () {
        sizeAppWindow();
        if (i === tries.length - 1) setTimeout(function () { appSizeReady = true; }, 600);
      }, ms);
    });
  }
  // "Default window size" button in Settings
  function resetAppSize() {
    settings.appSize = null;
    settings.appMaximized = false;
    saveSettings();
    sizeAppWindow(DEFAULT_APP_SIZE);
    setTimeout(function () { sizeAppWindow(DEFAULT_APP_SIZE); }, 300);
  }
  $('resetSizeBtn').addEventListener('click', resetAppSize);
  if (isInstalled()) startAppSizing();
  // Right after installing, the browser moves this page into the new app window without
  // reloading it, so size the window when the display mode switches to app mode.
  if (window.matchMedia) {
    var appMode = matchMedia('(display-mode: standalone)');
    var onModeChange = function () {
      applyTexts();
      if (isInstalled()) {
        settings.appMaximized = false;
        startAppSizing();
        if (!settings.appAutostartShown) showInstalledPage();
      }
    };
    if (appMode.addEventListener) appMode.addEventListener('change', onModeChange);
    else if (appMode.addListener) appMode.addListener(onModeChange);
  }

  applyTexts();
  updateBadge(true);
  resetToToday();
  // Clicking the QuickCal icon while the app is already open brings that window forward (manifest
  // launch_handler: focus-existing) instead of opening a second one. Show today, as on a fresh start.
  if ('launchQueue' in window) {
    try { window.launchQueue.setConsumer(function () { if (isCalendarPage(currentPage)) resetToToday(); }); } catch (e) { /* not supported */ }
  }

  // For pip.js, which brings the calendar back here when the floating window closes
  window.QuickCalApp = {
    refresh: function () { if (isCalendarPage(currentPage)) resetToToday(); }
  };

  if (PIP) {
    document.documentElement.classList.add('pip');   // the floating window: just the calendar
  } else if (!isInstalled()) {
    // In a browser tab: show the Getting started page on every visit, so it offers installation
    showWelcome();
  } else if (!settings.appAutostartShown) {
    // Installed app: never the Getting started page on start (it would show after every reboot),
    // only once, on the first start as an app, the page about Edge's choices and pinning
    showInstalledPage();
  }
})();
