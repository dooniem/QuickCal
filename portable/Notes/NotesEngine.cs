using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QuickCal.Notes
{
    /// <summary>A note as shown on a day: its key, the first and last day of this showing, and its half.</summary>
    public sealed class Showing
    {
        public string K, S, E;
        public int Slot;
    }

    /// <summary>
    /// Where the notes are in the calendar: which note covers each day in each half, the days repeating notes
    /// fall on, and room for more. The same rules as notes.js in the web app. No WPF here, so it can be tested alone.
    /// </summary>
    public sealed class NotesEngine
    {
        public const int MaxSpanDays = 62;
        public const int KeepDefault = 12, KeepMax = 520;

        private readonly Func<string, string, string> T;
        private readonly Func<DateTime, bool> isOffDay;

        /// <summary>key -> note (the store's notes).</summary>
        public Dictionary<string, Note> Index { get; private set; } = new Dictionary<string, Note>();

        private Dictionary<string, string>[] covers = { new Dictionary<string, string>(), new Dictionary<string, string>() };
        private HashSet<string> exc = new HashSet<string>();   // "series key|day" for the days a series has its own changed note
        private readonly Dictionary<int, Dictionary<string, List<Showing>>> recurByYear = new Dictionary<int, Dictionary<string, List<Showing>>>();

        public NotesFilter Filter { get; } = new NotesFilter();

        public NotesEngine(Func<string, string, string> t, Func<DateTime, bool> isOffDay)
        {
            T = t;
            this.isOffDay = isOffDay;
        }

        public void SetIndex(Dictionary<string, Note> notes)
        {
            Index = notes;
            Rebuild();
        }

        /// <summary>After any change to the notes: which note covers each day, in each half.</summary>
        public void Rebuild()
        {
            covers = new[] { new Dictionary<string, string>(), new Dictionary<string, string>() };
            exc = new HashSet<string>();
            recurByYear.Clear();
            foreach (string k in Index.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                Note n = Index[k];
                if (n.Repeat != null) continue;   // repeating notes: see NotesAt below
                int sl = Keys.SlotOf(k);
                string d = Keys.DayOf(k), end = n.End == "" ? d : n.End;
                for (int i = 0; i < MaxSpanDays && Keys.Compare(d, end) <= 0; i++, d = Keys.AddDays(d, 1))
                {
                    if (!covers[sl].ContainsKey(d)) covers[sl][d] = k;
                    if (!string.IsNullOrEmpty(n.Series)) exc.Add(n.Series + "|" + d);
                }
            }
        }

        /// <summary>Holidays or settings changed: the work days of repeating notes may move.</summary>
        public void Invalidate() => recurByYear.Clear();

        /// <summary>The notes shown on a day: [top, bottom], each null or a showing. Notes stored in a half keep
        /// it; a repeating note takes a free half.</summary>
        public Showing[] NotesAt(string dk)
        {
            var output = new Showing[2];
            int year = int.Parse(dk.Substring(0, 4), CultureInfo.InvariantCulture);
            var recs = RecurMap(year).TryGetValue(dk, out var list) ? new Queue<Showing>(list) : new Queue<Showing>();
            for (int sl = 0; sl < 2; sl++)
            {
                if (covers[sl].TryGetValue(dk, out string k))
                {
                    Note n = Index[k];
                    output[sl] = new Showing { K = k, S = Keys.DayOf(k), E = n.End == "" ? Keys.DayOf(k) : n.End, Slot = sl };
                }
            }
            for (int sl = 0; sl < 2; sl++)
            {
                if (output[sl] == null && recs.Count > 0)
                {
                    Showing r = recs.Dequeue();
                    output[sl] = new Showing { K = r.K, S = r.S, E = r.E, Slot = sl };
                }
            }
            return output;
        }

        public List<Showing> ListAt(string dk) => NotesAt(dk).Where(a => a != null).ToList();

        /// <summary>What the calendar shows: with the list's search and filter turned on for the calendar, only the
        /// notes that match. The others still take their place on the day.</summary>
        public Showing[] ShownAt(string dk) => NotesAt(dk).Select(a => a != null && (!Filter.Cal || Matches(a.K)) ? a : null).ToArray();

        public List<Showing> ShownList(string dk) => ShownAt(dk).Where(a => a != null).ToList();

        public bool HasNote(string dk) => ListAt(dk).Count > 0;

        /// <summary>Room for a note (self = its key when it is already stored) on days s..e in half sl.</summary>
        public bool Fits(string s, string e, int sl, string self)
        {
            string k2 = Keys.SlotKey(s, sl);
            if (Index.ContainsKey(k2) && k2 != self) return false;
            for (string d = s; Keys.Compare(d, e) <= 0; d = Keys.AddDays(d, 1))
            {
                if (covers[sl].TryGetValue(d, out string c) && c != self) return false;
                if (ListAt(d).Count(a => a.K != self) >= 2) return false;
            }
            return true;
        }

        /// <summary>A free half for days s..e (the preferred one first), or -1 when both are taken.</summary>
        public int FreeSlot(string s, string e, string self, int prefer = 0)
        {
            int[] order = prefer == 1 ? new[] { 1, 0 } : new[] { 0, 1 };
            foreach (int sl in order) if (Fits(s, e, sl, self)) return sl;
            return -1;
        }

        /// <summary>Last day a note with key k may run to: not into another note, and not longer than MaxSpanDays.</summary>
        public string MaxEnd(string k)
        {
            string s = Keys.DayOf(k), e = s;
            int sl = Keys.SlotOf(k);
            for (int i = 1; i < MaxSpanDays; i++)
            {
                string d = Keys.AddDays(s, i);
                if ((covers[sl].TryGetValue(d, out string c) && c != k) || Index.ContainsKey(Keys.SlotKey(d, sl)) ||
                    ListAt(d).Count(a => a.K != k) >= 2) break;
                e = d;
            }
            return e;
        }

        /// <summary>The covering note in half sl on day d, or null.</summary>
        public string CoverAt(int sl, string d) => covers[sl].TryGetValue(d, out string k) ? k : null;

        /// <summary>The series has its own changed note on this day.</summary>
        public bool IsException(string seriesKey, string day) => exc.Contains(seriesKey + "|" + day);

        // =====================================================================================
        // Repeating notes. The days they fall on are worked out when drawn (one year at a time) and never stored.
        // =====================================================================================

        private static DateTime NthWeekday(int y, int m, int nth, int wd)
        {
            if (nth < 0)
            {
                var last = new DateTime(y, m, DateTime.DaysInMonth(y, m));
                return last.AddDays(-((Keys.Wd(last) - wd + 7) % 7));
            }
            var first = new DateTime(y, m, 1);
            return first.AddDays((wd - Keys.Wd(first) + 7) % 7 + (nth - 1) * 7);
        }

        private bool OffDay(DateTime d)
        {
            try { return isOffDay != null ? isOffDay(d) : Keys.Wd(d) % 6 == 0; }
            catch (Exception) { return false; }
        }

        private DateTime Shifted(DateTime d, RepeatRule r)
        {
            int n = r.Shift;
            if (r.ShiftUnit == "days") return d.AddDays(n);
            if (r.ShiftUnit == "work")
            {
                if (n == 0)
                {
                    // 0: the next work day if this is not one
                    for (int g = 0; g < 30 && OffDay(d); g++) d = d.AddDays(1);
                    return d;
                }
                int step = n < 0 ? -1 : 1;
                for (int c = Math.Abs(n), i = 0; c > 0 && i < 400; i++)
                {
                    d = d.AddDays(step);
                    if (!OffDay(d)) c--;
                }
                return d;
            }
            if (r.ShiftUnit == "next")
            {
                DateTime x = d.AddDays(1);
                while (Keys.Wd(x) != r.ShiftWd) x = x.AddDays(1);
                return x;
            }
            return d;
        }

        /// <summary>The days (keys) a repeating note starting on k begins on, from..to.</summary>
        public List<string> Occurrences(string k, RepeatRule r, string from, string to)
        {
            k = Keys.DayOf(k);
            DateTime S = Keys.Parse(k);
            var output = new List<string>();
            int every = Math.Max(1, r.Every);
            string last = !string.IsNullOrEmpty(r.Until) && Keys.Compare(r.Until, to) < 0 ? r.Until : to;
            if (Keys.Compare(last, from) < 0 || Keys.Compare(last, k) < 0) return output;
            string stop = Keys.AddDays(last, 70);   // a base day this far after may still be moved back into range
            int guard = 0;

            void Push(DateTime b)
            {
                string o = Keys.Key(Shifted(b, r));
                if (Keys.Compare(o, k) >= 0 && Keys.Compare(o, from) >= 0 && Keys.Compare(o, last) <= 0 && !output.Contains(o) &&
                    !(r.Skip != null && r.Skip.Contains(o))) output.Add(o);
            }

            int skip = Math.Max(0, Keys.DaysBetween(k, from) - 70);   // jump ahead to near 'from'
            switch (r.Freq)
            {
                case "day":
                    for (long i = skip / every; guard++ < 5000; i++)
                    {
                        DateTime d = S.AddDays(i * every);
                        if (Keys.Compare(Keys.Key(d), stop) > 0) break;
                        Push(d);
                    }
                    break;
                case "week":
                    DateTime mon = S.AddDays(-((Keys.Wd(S) + 6) % 7));
                    List<int> days = r.Days != null && r.Days.Count > 0 ? r.Days : new List<int> { Keys.Wd(S) };
                    for (long w = skip / 7 / every * every; guard++ < 2000; w += every)
                    {
                        DateTime wk = mon.AddDays(w * 7);
                        if (Keys.Compare(Keys.Key(wk), stop) > 0) break;
                        foreach (int wd in days) Push(wk.AddDays((wd + 6) % 7));
                    }
                    break;
                case "month":
                    for (int m = 0; guard++ < 2000; m += every)
                    {
                        DateTime first = new DateTime(S.Year, S.Month, 1).AddMonths(m);
                        if (Keys.Compare(Keys.Key(first), stop) > 0) break;
                        DateTime b = r.By == "nth"
                            ? NthWeekday(first.Year, first.Month, r.Nth == 0 ? 1 : r.Nth, r.Wd)
                            : new DateTime(first.Year, first.Month, Math.Min(S.Day, DateTime.DaysInMonth(first.Year, first.Month)));
                        Push(b);
                    }
                    break;
                case "year":
                    for (int yr = 0; guard++ < 500; yr += every)
                    {
                        int yy = S.Year + yr;
                        if (yy > 9998) break;
                        var b = new DateTime(yy, S.Month, Math.Min(S.Day, DateTime.DaysInMonth(yy, S.Month)));
                        if (Keys.Compare(Keys.Key(b), stop) > 0) break;
                        Push(b);
                    }
                    break;
            }
            output.Sort(StringComparer.Ordinal);
            return output;
        }

        private Dictionary<string, List<Showing>> RecurMap(int y)
        {
            if (recurByYear.TryGetValue(y, out var cached)) return cached;
            var map = new Dictionary<string, List<Showing>>();
            string from = y.ToString("0000", CultureInfo.InvariantCulture) + "-01-01", to = y.ToString("0000", CultureInfo.InvariantCulture) + "-12-31";
            foreach (string k in Index.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                Note n = Index[k];
                if (n.Repeat == null) continue;
                int dur = n.End != "" ? Keys.DaysBetween(k, n.End) : 0;
                foreach (string o in Occurrences(k, n.Repeat, Keys.AddDays(from, -dur), to))
                {
                    string e = Keys.AddDays(o, dur);
                    for (string d = o; Keys.Compare(d, e) <= 0; d = Keys.AddDays(d, 1))
                    {
                        if (Keys.Compare(d, from) < 0 || Keys.Compare(d, to) > 0 || exc.Contains(k + "|" + d)) continue;   // a day the series has a changed note for
                        if (!map.TryGetValue(d, out var l)) map[d] = l = new List<Showing>();
                        l.Add(new Showing { K = k, S = o, E = e });
                    }
                }
            }
            recurByYear[y] = map;
            return map;
        }

        public List<string> NextOccurrences(string k, RepeatRule r, int count)
        {
            string today = Keys.Key(DateTime.Today);
            return Occurrences(k, r, today, Keys.AddDays(today, 3 * 366)).Take(count).ToList();
        }

        // =====================================================================================
        // Texts
        // =====================================================================================

        private static readonly string[] WdNo = { "søndag", "mandag", "tirsdag", "onsdag", "torsdag", "fredag", "lørdag" };
        private static readonly string[] WdEn = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        public string WdName(int i) => T(WdEn[i], WdNo[i]);
        public string WdShort(int i) => T(WdEn[i].Substring(0, 2), WdNo[i].Substring(0, 2));

        public string NthName(int nth)
        {
            switch (nth)
            {
                case 1: return T("first", "første");
                case 2: return T("second", "andre");
                case 3: return T("third", "tredje");
                case 4: return T("fourth", "fjerde");
                default: return T("last", "siste");
            }
        }

        /// <summary>"ma. 14.10."</summary>
        public string ShortDate(string k)
        {
            DateTime d = Keys.Parse(k);
            return WdShort(Keys.Wd(d)) + ". " + d.Day + "." + d.Month + ".";
        }

        /// <summary>"Every month, last Sunday + 4 work days"</summary>
        public string Summary(string k, RepeatRule r)
        {
            if (r == null) return "";
            DateTime S = Keys.Parse(k);
            int every = Math.Max(1, r.Every);
            string t;
            if (r.Freq == "day") t = every > 1 ? T("Every " + every + " days", "Hver " + every + ". dag") : T("Every day", "Hver dag");
            else if (r.Freq == "week")
            {
                var days = (r.Days != null && r.Days.Count > 0 ? r.Days : new List<int> { Keys.Wd(S) }).OrderBy(a => (a + 6) % 7);
                t = (every > 1 ? T("Every " + every + " weeks", "Hver " + every + ". uke") : T("Every week", "Hver uke")) + " " + T("on", "på") + " " +
                    string.Join(", ", days.Select(WdShort));
            }
            else if (r.Freq == "month")
            {
                t = every > 1 ? T("Every " + every + " months", "Hver " + every + ". måned") : T("Every month", "Hver måned");
                t += r.By == "nth" ? ", " + NthName(r.Nth) + " " + WdName(r.Wd) : " " + T("on the " + S.Day + ".", "den " + S.Day + ".");
            }
            else t = (every > 1 ? T("Every " + every + " years", "Hvert " + every + ". år") : T("Every year", "Hvert år")) + " " + S.Day + "." + S.Month + ".";
            int n = r.Shift, a = Math.Abs(n);
            if (r.ShiftUnit == "days" && n != 0) t += " " + (n > 0 ? "+ " : "− ") + a + " " + T(a == 1 ? "day" : "days", a == 1 ? "dag" : "dager");
            if (r.ShiftUnit == "work")
            {
                t += n != 0 ? " " + (n > 0 ? "+ " : "− ") + a + " " + T(a == 1 ? "work day" : "work days", a == 1 ? "arbeidsdag" : "arbeidsdager")
                            : T(", next work day if a day off", ", neste arbeidsdag hvis fridag");
            }
            if (r.ShiftUnit == "next") t += T(", then the first " + WdName(r.ShiftWd) + " after", ", så første " + WdName(r.ShiftWd) + " etter");
            if (!string.IsNullOrEmpty(r.Until)) t += T(", until ", ", til og med ") + ShortDate(r.Until);
            return t;
        }

        // =====================================================================================
        // How long notes are kept
        // =====================================================================================

        public static int CleanWeeks(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) return KeepDefault;
            return (int)Math.Min(Math.Floor(v), KeepMax);
        }

        /// <summary>The first day kept: Monday of this week, 'weeks' weeks back. Notes that ended before it are deleted.</summary>
        public static string OldestKept(int weeks)
        {
            DateTime t = DateTime.Today;
            DateTime monday = t.AddDays(-((Keys.Wd(t) + 6) % 7));
            return Keys.Key(monday.AddDays(-7 * weeks));
        }

        public static bool Expired(string date, string endDate, RepeatRule repeat, string limit)
        {
            if (repeat != null)
                return !string.IsNullOrEmpty(repeat.Until) &&
                       Keys.Compare(Keys.AddDays(repeat.Until, !string.IsNullOrEmpty(endDate) ? Keys.DaysBetween(date, endDate) : 0), limit) < 0;
            return Keys.Compare(!string.IsNullOrEmpty(endDate) ? endDate : Keys.DayOf(date), limit) < 0;
        }

        // =====================================================================================
        // Search and filter (the notes list)
        // =====================================================================================

        public bool Matches(string k)
        {
            if (!Index.TryGetValue(k, out Note n)) return false;
            string q = Filter.Q.Trim().ToLowerInvariant();
            if (Filter.Colors.Count > 0 && !Filter.Colors.Contains(n.Color ?? "")) return false;
            if (Filter.Pics != "" && (Filter.Pics == "with") != (n.Images.Count > 0)) return false;
            return q == "" || (n.Text ?? "").ToLowerInvariant().Contains(q);
        }

        public List<string> FilteredKeys() => Index.Keys.OrderBy(x => x, StringComparer.Ordinal).Where(Matches).ToList();
    }

    /// <summary>
    /// Live search: notes whose text contains what is typed (any case). Colors: none checked = all colors;
    /// "" stands for notes without a color. Pictures: "" = all notes, "with" / "without". Cal: the calendar shows
    /// only the notes that match too. Kept while QuickCal runs.
    /// </summary>
    public sealed class NotesFilter
    {
        public string Q = "";
        public HashSet<string> Colors = new HashSet<string>();
        public string Pics = "";
        public bool Cal;

        public bool Active => Q.Trim() != "" || Colors.Count > 0 || Pics != "";
    }
}
