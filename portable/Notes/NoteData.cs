using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace QuickCal.Notes
{
    /// <summary>
    /// A note on one or more days. The same fields (and JSON names) as in the web app, so backups and the
    /// Dropbox sync file (notes.json) work between QuickCal on the web and on Windows.
    /// A day holds at most two notes: one in the top half and one in the bottom half (slot 0 and 1).
    /// A note's key is the day it starts on, with a 'b' after it in the bottom half: "2026-10-14", "2026-10-14b".
    /// </summary>
    public sealed class Note
    {
        [JsonIgnore] public string Date { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("color")] public string Color { get; set; } = "";
        /// <summary>Picture file names: the SHA-256 of the content + .jpg or .png (the same names as in Dropbox).</summary>
        [JsonPropertyName("images")] public List<string> Images { get; set; } = new List<string>();
        [JsonPropertyName("endDate")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string EndDate { get; set; }
        [JsonPropertyName("repeat")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public RepeatRule Repeat { get; set; }
        /// <summary>A changed day of a repeating note: the key of the series it belongs to.</summary>
        [JsonPropertyName("series")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string Series { get; set; }
        /// <summary>When it was last changed (milliseconds since 1970, like JavaScript's Date.now()).</summary>
        [JsonPropertyName("updated")] public long Updated { get; set; }

        [JsonIgnore] public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && string.IsNullOrEmpty(Color) && Images.Count == 0 && Repeat == null;

        /// <summary>The last day it covers ("" = only its first day).</summary>
        [JsonIgnore] public string End => !string.IsNullOrEmpty(EndDate) && string.CompareOrdinal(EndDate, Keys.DayOf(Date)) > 0 ? EndDate : "";

        public Note Copy()
        {
            return new Note
            {
                Date = Date, Text = Text ?? "", Color = Color ?? "", Images = new List<string>(Images ?? new List<string>()),
                EndDate = EndDate, Repeat = Repeat?.Copy(), Series = Series, Updated = Updated
            };
        }

        /// <summary>Text, color, days, rule or pictures differ.</summary>
        public static bool Differs(Note a, Note b)
        {
            return (a.Text ?? "") != (b.Text ?? "") || (a.Color ?? "") != (b.Color ?? "") || (a.EndDate ?? "") != (b.EndDate ?? "") ||
                   !RepeatRule.Same(a.Repeat, b.Repeat) || !a.Images.SequenceEqual(b.Images);
        }
    }

    /// <summary>
    /// A repeating note is stored once, on the day it starts from, with a rule:
    ///   freq: day | week | month | year, every: n, days: weekdays 0-6 (week, Sunday = 0),
    ///   by: date | nth (month), nth: 1-4 or -1 for the last, wd: weekday (nth),
    ///   shiftUnit: "" | days | work | next, shift: n (days/work days, may be negative), shiftWd: weekday (next),
    ///   until: "YYYY-MM-DD" or "", skip: days the series leaves out.
    /// Example: "4 work days after the last Sunday of the month" = month, nth -1, wd 0, work, 4.
    /// </summary>
    public sealed class RepeatRule
    {
        [JsonPropertyName("freq")] public string Freq { get; set; } = "week";
        [JsonPropertyName("every")] public int Every { get; set; } = 1;
        [JsonPropertyName("days")] public List<int> Days { get; set; } = new List<int>();
        [JsonPropertyName("by")] public string By { get; set; } = "date";
        [JsonPropertyName("nth")] public int Nth { get; set; } = -1;
        [JsonPropertyName("wd")] public int Wd { get; set; }
        [JsonPropertyName("shiftUnit")] public string ShiftUnit { get; set; } = "";
        [JsonPropertyName("shift")] public int Shift { get; set; }
        [JsonPropertyName("shiftWd")] public int ShiftWd { get; set; } = 4;
        [JsonPropertyName("until")] public string Until { get; set; } = "";
        [JsonPropertyName("skip")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string> Skip { get; set; }

        public RepeatRule Copy()
        {
            var c = (RepeatRule)MemberwiseClone();
            c.Days = new List<int>(Days ?? new List<int>());
            c.Skip = Skip == null ? null : new List<string>(Skip);
            return c;
        }

        public static bool Same(RepeatRule a, RepeatRule b)
        {
            if (a == null || b == null) return a == b;
            return a.Freq == b.Freq && a.Every == b.Every && (a.Days ?? new List<int>()).SequenceEqual(b.Days ?? new List<int>()) &&
                   a.By == b.By && a.Nth == b.Nth && a.Wd == b.Wd && (a.ShiftUnit ?? "") == (b.ShiftUnit ?? "") && a.Shift == b.Shift &&
                   a.ShiftWd == b.ShiftWd && (a.Until ?? "") == (b.Until ?? "") &&
                   (a.Skip ?? new List<string>()).SequenceEqual(b.Skip ?? new List<string>());
        }
    }

    /// <summary>The note colors: not used by the calendar itself (holiday red, today/week blue, birthday gold).</summary>
    public static class NoteColors
    {
        public sealed class Entry
        {
            public string Id, Hex, En, No;
            public Entry(string id, string hex, string en, string no) { Id = id; Hex = hex; En = en; No = no; }
        }

        public static readonly Entry[] All =
        {
            new Entry("maroon", "#7b1e2b", "Maroon", "Vinrød"),
            new Entry("orange", "#e8871e", "Orange", "Oransje"),
            new Entry("green", "#2e9e5b", "Green", "Grønn"),
            new Entry("purple", "#8e44ad", "Purple", "Lilla"),
            new Entry("teal", "#11808a", "Teal", "Petrol"),
            new Entry("brown", "#8a6a4a", "Brown", "Brun"),
        };

        /// <summary>"" and every color id: no color first, then 1-6.</summary>
        public static IEnumerable<string> IdsWithNone => new[] { "" }.Concat(All.Select(c => c.Id));

        public static string Hex(string id) => All.FirstOrDefault(c => c.Id == id)?.Hex;

        /// <summary>No color = 0, then 1-6.</summary>
        public static int Number(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i + 1;
            return 0;
        }
    }

    /// <summary>Day keys ("2026-10-14", "2026-10-14b") and date helpers.</summary>
    public static class Keys
    {
        public static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static DateTime Parse(string k) =>
            new DateTime(int.Parse(k.Substring(0, 4), CultureInfo.InvariantCulture), int.Parse(k.Substring(5, 2), CultureInfo.InvariantCulture),
                         int.Parse(k.Substring(8, 2), CultureInfo.InvariantCulture));

        public static string DayOf(string k) => k.Length > 10 ? k.Substring(0, 10) : k;
        public static int SlotOf(string k) => k.Length > 10 ? 1 : 0;
        public static string SlotKey(string day, int slot) => slot == 1 ? day + "b" : day;
        public static string AddDays(string k, int n) => Key(Parse(k).AddDays(n));
        public static int DaysBetween(string a, string b) => (int)Math.Round((Parse(b) - Parse(a)).TotalDays);
        public static int Compare(string a, string b) => string.CompareOrdinal(a, b);

        /// <summary>A valid note key: yyyy-mm-dd, with an optional 'b'.</summary>
        public static bool IsNoteKey(string k)
        {
            if (k == null || (k.Length != 10 && !(k.Length == 11 && k[10] == 'b'))) return false;
            return DateTime.TryParseExact(k.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        public static bool IsDayKey(string k) =>
            k != null && k.Length == 10 && DateTime.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Weekday as in JavaScript: Sunday = 0 ... Saturday = 6.</summary>
        public static int Wd(DateTime d) => (int)d.DayOfWeek;
    }
}
