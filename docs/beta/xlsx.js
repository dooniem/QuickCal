/* QuickCal Web (beta) - a small Excel (.xlsx) writer for the notes export, no libraries needed.
   One sheet: a bold header row, then one row per note. Dates are real Excel dates shown as dd.mm.yy, numbers
   are numbers and text is text, so the same columns can be read back by a future import. Pictures are placed
   inside their cells (they move, sort and hide with the row).
   An .xlsx file is a zip of a few XML files; they are stored uncompressed, which Excel accepts. */
window.QuickCalXlsx = (function () {
  'use strict';

  // ---------- Zip (stored, no compression) ----------
  var crcTable = null;
  function crc32(bytes) {
    if (!crcTable) {
      crcTable = new Uint32Array(256);
      for (var n = 0; n < 256; n++) {
        var c = n;
        for (var k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
        crcTable[n] = c >>> 0;
      }
    }
    var crc = 0xFFFFFFFF;
    for (var i = 0; i < bytes.length; i++) crc = crcTable[(crc ^ bytes[i]) & 0xFF] ^ (crc >>> 8);
    return (crc ^ 0xFFFFFFFF) >>> 0;
  }
  function zip(files) {   // files: [{ name, text }] or [{ name, data: Uint8Array }]
    var enc = new TextEncoder(), parts = [], central = [], offset = 0;
    var now = new Date();
    var dosTime = (now.getHours() << 11) | (now.getMinutes() << 5) | (now.getSeconds() >> 1);
    var dosDate = ((now.getFullYear() - 1980) << 9) | ((now.getMonth() + 1) << 5) | now.getDate();
    files.forEach(function (f) {
      var name = enc.encode(f.name), data = f.data || enc.encode(f.text), crc = crc32(data);
      var local = new DataView(new ArrayBuffer(30));
      local.setUint32(0, 0x04034b50, true); local.setUint16(4, 20, true); local.setUint16(6, 0x0800, true);
      local.setUint16(8, 0, true); local.setUint16(10, dosTime, true); local.setUint16(12, dosDate, true);
      local.setUint32(14, crc, true); local.setUint32(18, data.length, true); local.setUint32(22, data.length, true);
      local.setUint16(26, name.length, true); local.setUint16(28, 0, true);
      var cen = new DataView(new ArrayBuffer(46));
      cen.setUint32(0, 0x02014b50, true); cen.setUint16(4, 20, true); cen.setUint16(6, 20, true); cen.setUint16(8, 0x0800, true);
      cen.setUint16(10, 0, true); cen.setUint16(12, dosTime, true); cen.setUint16(14, dosDate, true);
      cen.setUint32(16, crc, true); cen.setUint32(20, data.length, true); cen.setUint32(24, data.length, true);
      cen.setUint16(28, name.length, true); cen.setUint32(42, offset, true);
      parts.push(local, name, data);
      central.push(cen, name);
      offset += 30 + name.length + data.length;
    });
    var size = central.reduce(function (s, p) { return s + p.byteLength; }, 0);
    var end = new DataView(new ArrayBuffer(22));
    end.setUint32(0, 0x06054b50, true); end.setUint16(8, files.length, true); end.setUint16(10, files.length, true);
    end.setUint32(12, size, true); end.setUint32(16, offset, true);
    return new Blob(parts.concat(central, [end]), { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  }

  // ---------- Sheet ----------
  function esc(s) {
    return String(s).replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, '')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }
  function colName(i) { var s = ''; i++; while (i) { var m = (i - 1) % 26; s = String.fromCharCode(65 + m) + s; i = (i - m - 1) / 26; } return s; }

  // Excel's day number for a date (days since 30.12.1899)
  function serial(d) { return Math.round((Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()) - Date.UTC(1899, 11, 30)) / 86400000); }

  // Pictures: fitted inside their cell, keeping the shape. Sizes in pixels, written in EMU (9525 per pixel).
  function colPx(chars) { return Math.floor(chars * 7 + 5); }
  function drawingXml(images, widths, rowHeights) {
    var EMU = 9525, pad = 3;
    return '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
      '<xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">' +
      images.map(function (im, i) {
        var cw = colPx(widths[im.col] || 8.43), ch = Math.round((rowHeights[im.row] || 15) * 96 / 72);
        var s = Math.min((cw - 2 * pad) / im.w, (ch - 2 * pad) / im.h);
        var w = Math.max(1, Math.floor(im.w * s)), h = Math.max(1, Math.floor(im.h * s));
        var x = Math.floor((cw - w) / 2), y = Math.floor((ch - h) / 2);
        return '<xdr:twoCellAnchor editAs="twoCell">' +
          '<xdr:from><xdr:col>' + im.col + '</xdr:col><xdr:colOff>' + x * EMU + '</xdr:colOff><xdr:row>' + im.row + '</xdr:row><xdr:rowOff>' + y * EMU + '</xdr:rowOff></xdr:from>' +
          '<xdr:to><xdr:col>' + im.col + '</xdr:col><xdr:colOff>' + (x + w) * EMU + '</xdr:colOff><xdr:row>' + im.row + '</xdr:row><xdr:rowOff>' + (y + h) * EMU + '</xdr:rowOff></xdr:to>' +
          '<xdr:pic><xdr:nvPicPr><xdr:cNvPr id="' + (i + 2) + '" name="' + esc(im.name || ('Picture ' + (i + 1))) + '"/><xdr:cNvPicPr><a:picLocks noChangeAspect="1"/></xdr:cNvPicPr></xdr:nvPicPr>' +
          '<xdr:blipFill><a:blip r:embed="rId' + (i + 1) + '"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>' +
          '<xdr:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="' + w * EMU + '" cy="' + h * EMU + '"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic>' +
          '<xdr:clientData/></xdr:twoCellAnchor>';
      }).join('') + '</xdr:wsDr>';
  }

  // rows: array of arrays; the first row is the header. A cell is text, a number or a Date (shown dd.mm.yy).
  // widths: column widths in characters. wrap: column indexes whose text may run over several lines.
  // images: [{ row, col, data: Uint8Array, type: 'image/jpeg' | 'image/png', w, h }] (row 0 = header).
  // rowHeights: { rowIndex: points } for rows that need room for pictures.
  function write(rows, opts) {
    opts = opts || {};
    var widths = opts.widths || [], wrap = opts.wrap || [], images = opts.images || [], rowHeights = opts.rowHeights || {};
    var cols = widths.map(function (w, i) { return '<col min="' + (i + 1) + '" max="' + (i + 1) + '" width="' + w + '" customWidth="1"/>'; }).join('');
    var body = rows.map(function (row, r) {
      var ht = rowHeights[r] ? ' ht="' + rowHeights[r] + '" customHeight="1"' : '';
      return '<row r="' + (r + 1) + '"' + ht + '>' + row.map(function (v, c) {
        var ref = colName(c) + (r + 1), style = r === 0 ? 1 : wrap.indexOf(c) >= 0 ? 2 : 3;
        if (v === null || v === undefined || v === '') return '<c r="' + ref + '" s="' + style + '"/>';
        if (v instanceof Date) return '<c r="' + ref + '" s="4"><v>' + serial(v) + '</v></c>';
        if (typeof v === 'number') return '<c r="' + ref + '" s="' + style + '"><v>' + v + '</v></c>';
        return '<c r="' + ref + '" s="' + style + '" t="inlineStr"><is><t xml:space="preserve">' + esc(v) + '</t></is></c>';
      }).join('') + '</row>';
    }).join('');
    var sheet = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
      '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">' +
      '<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>' +
      (cols ? '<cols>' + cols + '</cols>' : '') + '<sheetData>' + body + '</sheetData>' +
      (rows.length > 1 ? '<autoFilter ref="A1:' + colName(rows[0].length - 1) + rows.length + '"/>' : '') +
      (images.length ? '<drawing r:id="rId1"/>' : '') +
      '</worksheet>';
    var media = images.map(function (im, i) {
      return { name: 'xl/media/image' + (i + 1) + (im.type === 'image/png' ? '.png' : '.jpeg'), data: im.data };
    });
    var sheetName = esc(opts.sheet || 'Sheet1');
    var files = [
      { name: '[Content_Types].xml', text: '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
        '<Default Extension="xml" ContentType="application/xml"/>' +
        '<Default Extension="jpeg" ContentType="image/jpeg"/>' +
        '<Default Extension="png" ContentType="image/png"/>' +
        (images.length ? '<Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>' : '') +
        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>' +
        '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>' +
        '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>' +
        '</Types>' },
      { name: '_rels/.rels', text: '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>' +
        '</Relationships>' },
      { name: 'xl/workbook.xml', text: '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">' +
        '<sheets><sheet name="' + sheetName + '" sheetId="1" r:id="rId1"/></sheets>' +
        (rows.length > 1 ? '<definedNames><definedName name="_xlnm._FilterDatabase" localSheetId="0" hidden="1">\'' + sheetName + '\'!$A$1:$' +
          colName(rows[0].length - 1) + '$' + rows.length + '</definedName></definedNames>' : '') +
        '</workbook>' },
      { name: 'xl/_rels/workbook.xml.rels', text: '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>' +
        '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>' +
        '</Relationships>' },
      { name: 'xl/styles.xml', text: '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">' +
        '<numFmts count="1"><numFmt numFmtId="164" formatCode="dd.mm.yy"/></numFmts>' +
        '<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>' +
        '<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>' +
        '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>' +
        '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>' +
        '<cellXfs count="5">' +
        '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>' +
        '<xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>' +
        '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>' +
        '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top"/></xf>' +
        '<xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="left" vertical="top"/></xf>' +
        '</cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>' },
      { name: 'xl/worksheets/sheet1.xml', text: sheet }
    ];
    if (images.length) {
      files.push(
        { name: 'xl/worksheets/_rels/sheet1.xml.rels', text: '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
          '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
          '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>' +
          '</Relationships>' },
        { name: 'xl/drawings/drawing1.xml', text: drawingXml(images, widths, rowHeights) },
        { name: 'xl/drawings/_rels/drawing1.xml.rels', text: '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
          '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
          media.map(function (m, i) {
            return '<Relationship Id="rId' + (i + 1) + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/' + m.name.split('/').pop() + '"/>';
          }).join('') + '</Relationships>' });
      files = files.concat(media);
    }
    return zip(files);
  }

  // Where to save. Edge and Chrome on a PC: a "Save as" window, so the file goes where the user wants and
  // the browser's download button does not appear in the app's title bar. Elsewhere: an ordinary download.
  // Must be called right in the click (the browser only opens "Save as" for a click). Resolves to a
  // save(blob) function, or rejects with an AbortError if the user cancels.
  function target(fileName) {
    if (window.showSaveFilePicker) {
      try {
        return window.showSaveFilePicker({
          suggestedName: fileName,
          types: [{ description: 'Excel', accept: { 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': ['.xlsx'] } }]
        }).then(function (handle) {
          return function (blob) {
            return handle.createWritable().then(function (w) { return w.write(blob).then(function () { return w.close(); }); });
          };
        });
      } catch (e) { /* fall back to a download */ }
    }
    return Promise.resolve(function (blob) { download(blob, fileName); return Promise.resolve(); });
  }

  // Let the browser save the file (the Downloads folder, as with any download)
  function download(blob, fileName) {
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
  }

  return { write: write, target: target, download: download };
})();
