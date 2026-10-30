using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickCal.Notes
{
    /// <summary>
    /// Backup: all notes, pictures and color names in one .json file, and restore from it. The same file as the
    /// web app makes, so a backup from one can be restored in the other. A .json file rather than Excel: it brings
    /// everything back exactly as it was, and nobody is tempted to edit it. The Excel export is for reading and sharing.
    /// </summary>
    public static class NotesBackup
    {
        public const string Kind = "quickcal-notes-backup";

        public static void Write(NoteStore store, string path)
        {
            var list = new JsonArray();
            foreach (Note n in store.Notes.Values.OrderBy(n => n.Date, StringComparer.Ordinal))
            {
                var o = new JsonObject { ["date"] = n.Date, ["text"] = n.Text ?? "", ["color"] = n.Color ?? "", ["updated"] = n.Updated };
                if (!string.IsNullOrEmpty(n.EndDate)) o["endDate"] = n.EndDate;
                if (n.Repeat != null) o["repeat"] = JsonNode.Parse(JsonSerializer.Serialize(n.Repeat));
                var images = new JsonArray();
                foreach (string p in n.Images)
                {
                    byte[] bytes = store.ReadPicture(p);
                    if (bytes != null) images.Add("data:" + (p.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg") + ";base64," + Convert.ToBase64String(bytes));
                }
                o["images"] = images;
                list.Add(o);
            }
            var file = new JsonObject
            {
                ["kind"] = Kind,
                ["version"] = 1,
                ["created"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
                ["colorNames"] = new JsonObject(store.ColorNames.Select(p => new KeyValuePair<string, JsonNode>(p.Key, JsonValue.Create(p.Value)))),
                ["notes"] = list
            };
            File.WriteAllText(path, file.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), new UTF8Encoding(false));
        }

        public sealed class Contents
        {
            public DateTime? Created;
            /// <summary>The notes, with their pictures as (bytes, png).</summary>
            public List<(Note Note, List<(byte[] Bytes, bool Png)> Pictures)> Notes = new List<(Note, List<(byte[], bool)>)>();
            public Dictionary<string, string> ColorNames = new Dictionary<string, string>();
        }

        /// <summary>Reads a backup file; null when it is not one.</summary>
        public static Contents Read(string path)
        {
            JsonObject data;
            try { data = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
            catch (Exception) { return null; }
            if (data == null || (string)data["kind"] != Kind || !(data["notes"] is JsonArray notes)) return null;

            var result = new Contents();
            try
            {
                if (data["created"] is JsonValue c && c.TryGetValue(out string created) && DateTime.TryParse(created, out DateTime when)) result.Created = when;
            }
            catch (Exception) { /* no date */ }

            foreach (JsonNode item in notes)
            {
                if (!(item is JsonObject o)) continue;
                string date = Text(o["date"]), text = Text(o["text"]);
                if (!Keys.IsNoteKey(date) || text == null) continue;
                var n = new Note { Date = date, Text = text, Color = NoteColors.Hex(Text(o["color"]) ?? "") != null ? Text(o["color"]) : "" };
                string end = Text(o["endDate"]);
                if (Keys.IsDayKey(end) && Keys.Compare(end, Keys.DayOf(date)) > 0) n.EndDate = end;
                if (o["repeat"] is JsonObject r)
                {
                    try { n.Repeat = JsonSerializer.Deserialize<RepeatRule>(r.ToJsonString()); } catch (Exception) { n.Repeat = null; }
                    if (n.Repeat != null) { n.Repeat.Days ??= new List<int>(); n.Repeat.Until ??= ""; n.Repeat.ShiftUnit ??= ""; }
                }
                var pictures = new List<(byte[], bool)>();
                if (o["images"] is JsonArray imgs)
                {
                    foreach (JsonNode im in imgs.Take(NoteStore.MaxImages))
                    {
                        string url = Text(im);
                        int comma = url?.IndexOf(',') ?? -1;
                        if (url == null || !url.StartsWith("data:", StringComparison.Ordinal) || comma < 0) continue;
                        try { pictures.Add((Convert.FromBase64String(url.Substring(comma + 1)), url.StartsWith("data:image/png", StringComparison.Ordinal))); }
                        catch (Exception) { /* a damaged picture is left out */ }
                    }
                }
                result.Notes.Add((n, pictures));
            }
            if (data["colorNames"] is JsonObject names)
                foreach (var p in names)
                {
                    string v = Text(p.Value);
                    if (!string.IsNullOrWhiteSpace(v)) result.ColorNames[p.Key] = v;
                }
            return result;
        }

        private static string Text(JsonNode n)
        {
            try { return n is JsonValue v && v.TryGetValue(out string s) ? s : null; }
            catch (Exception) { return null; }
        }
    }
}
