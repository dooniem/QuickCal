using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickCal.Notes
{
    /// <summary>
    /// A small Excel (.xlsx) writer for the notes export, the same as xlsx.js in the web app.
    /// One sheet: a bold header row, then one row per note. Dates are real Excel dates shown as dd.mm.yy, numbers
    /// are numbers and text is text. Pictures are placed inside their cells (they move, sort and hide with the row).
    /// </summary>
    public static class XlsxWriter
    {
        public sealed class Picture
        {
            public int Row, Col, W, H;
            public byte[] Data;
            public bool Png;
            public string Name;
        }

        private static string Esc(string s) =>
            Regex.Replace(s ?? "", "[\u0000-\u0008\u000B\u000C\u000E-\u001F]", "")
                .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static string ColName(int i)
        {
            string s = "";
            i++;
            while (i > 0)
            {
                int m = (i - 1) % 26;
                s = (char)('A' + m) + s;
                i = (i - m - 1) / 26;
            }
            return s;
        }

        /// <summary>Excel's day number for a date (days since 30.12.1899).</summary>
        private static int Serial(DateTime d) => (int)Math.Round((d.Date - new DateTime(1899, 12, 30)).TotalDays);

        private static int ColPx(double chars) => (int)Math.Floor(chars * 7 + 5);

        private static string F(double v) => v.ToString(CultureInfo.InvariantCulture);

        private static string DrawingXml(IList<Picture> images, IList<double> widths, IDictionary<int, double> rowHeights)
        {
            const long Emu = 9525;
            const int Pad = 3;
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            for (int i = 0; i < images.Count; i++)
            {
                Picture im = images[i];
                int cw = ColPx(im.Col < widths.Count ? widths[im.Col] : 8.43);
                int ch = (int)Math.Round((rowHeights.TryGetValue(im.Row, out double rh) ? rh : 15) * 96 / 72);
                double s = Math.Min((cw - 2.0 * Pad) / im.W, (ch - 2.0 * Pad) / im.H);
                int w = Math.Max(1, (int)Math.Floor(im.W * s)), h = Math.Max(1, (int)Math.Floor(im.H * s));
                int x = (cw - w) / 2, y = (ch - h) / 2;
                sb.Append("<xdr:twoCellAnchor editAs=\"twoCell\">")
                  .Append("<xdr:from><xdr:col>").Append(im.Col).Append("</xdr:col><xdr:colOff>").Append(x * Emu).Append("</xdr:colOff><xdr:row>").Append(im.Row).Append("</xdr:row><xdr:rowOff>").Append(y * Emu).Append("</xdr:rowOff></xdr:from>")
                  .Append("<xdr:to><xdr:col>").Append(im.Col).Append("</xdr:col><xdr:colOff>").Append((x + w) * Emu).Append("</xdr:colOff><xdr:row>").Append(im.Row).Append("</xdr:row><xdr:rowOff>").Append((y + h) * Emu).Append("</xdr:rowOff></xdr:to>")
                  .Append("<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"").Append(i + 2).Append("\" name=\"").Append(Esc(im.Name ?? "Picture " + (i + 1))).Append("\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr>")
                  .Append("<xdr:blipFill><a:blip r:embed=\"rId").Append(i + 1).Append("\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>")
                  .Append("<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(w * Emu).Append("\" cy=\"").Append(h * Emu).Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic>")
                  .Append("<xdr:clientData/></xdr:twoCellAnchor>");
            }
            sb.Append("</xdr:wsDr>");
            return sb.ToString();
        }

        /// <summary>
        /// rows: the first row is the header. A cell is text, a number or a DateTime (shown dd.mm.yy).
        /// widths: column widths in characters. wrap: columns whose text may run over several lines.
        /// rowHeights: points, for rows that need room for pictures.
        /// </summary>
        public static void Write(Stream output, IList<IList<object>> rows, string sheetName, IList<double> widths, ICollection<int> wrap,
                                 IList<Picture> images, IDictionary<int, double> rowHeights)
        {
            string cols = string.Concat(widths.Select((w, i) => "<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" + F(w) + "\" customWidth=\"1\"/>"));
            var body = new StringBuilder();
            for (int r = 0; r < rows.Count; r++)
            {
                string ht = rowHeights.TryGetValue(r, out double h) ? " ht=\"" + F(h) + "\" customHeight=\"1\"" : "";
                body.Append("<row r=\"").Append(r + 1).Append('"').Append(ht).Append('>');
                for (int c = 0; c < rows[r].Count; c++)
                {
                    object v = rows[r][c];
                    string reference = ColName(c) + (r + 1);
                    int style = r == 0 ? 1 : wrap.Contains(c) ? 2 : 3;
                    if (v == null || (v is string e && e.Length == 0)) body.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style).Append("\"/>");
                    else if (v is DateTime d) body.Append("<c r=\"").Append(reference).Append("\" s=\"4\"><v>").Append(Serial(d)).Append("</v></c>");
                    else if (v is int || v is double) body.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style).Append("\"><v>").Append(F(Convert.ToDouble(v, CultureInfo.InvariantCulture))).Append("</v></c>");
                    else body.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(Esc(v.ToString())).Append("</t></is></c>");
                }
                body.Append("</row>");
            }
            string lastCol = ColName(rows[0].Count - 1);
            string sheet = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>" +
                (cols.Length > 0 ? "<cols>" + cols + "</cols>" : "") + "<sheetData>" + body + "</sheetData>" +
                (rows.Count > 1 ? "<autoFilter ref=\"A1:" + lastCol + rows.Count + "\"/>" : "") +
                (images.Count > 0 ? "<drawing r:id=\"rId1\"/>" : "") +
                "</worksheet>";
            string name = Esc(sheetName);

            var files = new List<KeyValuePair<string, byte[]>>();
            void Add(string path, string text) => files.Add(new KeyValuePair<string, byte[]>(path, Encoding.UTF8.GetBytes(text)));

            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
                "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
                (images.Count > 0 ? "<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>" : "") +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"" + name + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                (rows.Count > 1 ? "<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">'" + name + "'!$A$1:$" + lastCol + "$" + rows.Count + "</definedName></definedNames>" : "") +
                "</workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");
            Add("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"dd.mm.yy\"/></numFmts>" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"5\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\"/></xf>" +
                "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"top\"/></xf>" +
                "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            Add("xl/worksheets/sheet1.xml", sheet);
            if (images.Count > 0)
            {
                var media = images.Select((im, i) => "image" + (i + 1) + (im.Png ? ".png" : ".jpeg")).ToList();
                Add("xl/worksheets/_rels/sheet1.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/>" +
                    "</Relationships>");
                Add("xl/drawings/drawing1.xml", DrawingXml(images, widths, rowHeights));
                Add("xl/drawings/_rels/drawing1.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    string.Concat(media.Select((m, i) => "<Relationship Id=\"rId" + (i + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/" + m + "\"/>")) +
                    "</Relationships>");
                for (int i = 0; i < images.Count; i++) files.Add(new KeyValuePair<string, byte[]>("xl/media/" + media[i], images[i].Data));
            }

            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var f in files)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(f.Key, CompressionLevel.Optimal);
                    using (Stream s = entry.Open()) s.Write(f.Value, 0, f.Value.Length);
                }
            }
        }
    }
}
