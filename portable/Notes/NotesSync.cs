using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace QuickCal.Notes
{
    public enum SyncState { Off, Lost, Busy, Offline, Error, Ok }

    /// <summary>
    /// Sync with Dropbox, the same way as the web app, so the notes are the same on every device. QuickCal's folder
    /// in the user's Dropbox holds notes.json (every note, with the names of its pictures) and one file per picture
    /// in img/, named by its content, so each picture is sent only once. Each device merges note by note: the
    /// latest change wins. A deleted note leaves a mark (which and when) so the delete reaches the other devices too.
    /// The notes on this PC stay the real copy: without Dropbox, or offline, everything works as before.
    /// </summary>
    public sealed class NotesSync
    {
        private const string SyncFile = "/notes.json", SyncKind = "quickcal-notes-sync";

        private readonly NoteStore store;
        private readonly Func<string, string, string> T;
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly DispatcherTimer every5Minutes = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        private Task syncing;
        private bool syncAgain, offline;

        public DropboxClient Dropbox { get; }
        public string Error { get; private set; } = "";

        /// <summary>The notes on this PC were changed by the sync: draw the calendar and the list again.</summary>
        public event Action NotesChanged;
        /// <summary>The state changed (for the Dropbox button and page).</summary>
        public event Action StateChanged;
        /// <summary>A shorter "keep notes" came from another device: delete the older notes here too.</summary>
        public event Action KeepShortened;

        /// <summary>A note is open: sync after it closes, so nothing changes under the user's hands.</summary>
        public Func<bool> Busy { get; set; } = () => false;

        public NotesSync(NoteStore store, Func<string, string, string> t)
        {
            this.store = store;
            T = t;
            Dropbox = new DropboxClient(store);
            timer.Tick += (s, e) => { timer.Stop(); _ = SyncNow(); };
            every5Minutes.Tick += (s, e) => Soon(0);
            store.Changed += () => Soon();
        }

        public void Start()
        {
            every5Minutes.Start();
            Soon(0);
        }

        public SyncState State
        {
            get
            {
                if (!Dropbox.Connected) return Dropbox.Lost ? SyncState.Lost : SyncState.Off;
                return syncing != null ? SyncState.Busy : offline ? SyncState.Offline : Error != "" ? SyncState.Error : SyncState.Ok;
            }
        }

        public long LastSynced => store.LastSynced;

        public void Soon(int ms = 3000)
        {
            if (!Dropbox.Connected || !store.Available) return;
            timer.Stop();
            timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
            timer.Start();
        }

        private void Tell()
        {
            try { StateChanged?.Invoke(); } catch (Exception) { /* only a display */ }
        }

        public Task SyncNow()
        {
            timer.Stop();
            if (!Dropbox.Connected || !store.Available) { Tell(); return Task.CompletedTask; }
            if (syncing != null) { syncAgain = true; return syncing; }
            if (Busy()) { Soon(4000); return Task.CompletedTask; }
            syncing = Run();
            Tell();
            return syncing;
        }

        // Nothing here may stop the app: every failure ends as a state for the button, and the next sync tries
        // again (on the next change, when the window is shown, or within 5 minutes).
        private async Task Run()
        {
            try
            {
                await SyncOnce(0);
                Error = "";
                offline = false;
                store.LastSynced = Keys.Now();
                store.Save();
            }
            catch (Exception ex)
            {
                var e = ex as DropboxException;
                offline = e != null && e.Offline;
                string s = e?.Summary ?? "";
                Error = e != null && e.SignedOut ? ""
                    : e != null && e.Offline ? ""
                    : s.Contains("insufficient_space") ? T("Dropbox is full", "Dropbox er full")
                    : e != null && e.Newer ? T("a newer QuickCal has synced, update this one", "en nyere QuickCal har synket, oppdater denne")
                    : e != null && e.Busy ? T("Dropbox is busy", "Dropbox er opptatt")
                    : T("error", "feil");
            }
            finally
            {
                syncing = null;
                Tell();
                if (syncAgain) { syncAgain = false; Soon(500); }
            }
        }

        // ---------- notes.json ----------

        /// <summary>A note as it is kept in notes.json, in the same field order as the web app writes it.</summary>
        private static JsonObject Shared(Note n)
        {
            var o = new JsonObject
            {
                ["text"] = n.Text ?? "",
                ["color"] = n.Color ?? "",
                ["images"] = new JsonArray(n.Images.Select(p => (JsonNode)JsonValue.Create(p)).ToArray()),
                ["updated"] = n.Updated
            };
            if (!string.IsNullOrEmpty(n.EndDate)) o["endDate"] = n.EndDate;
            if (n.Repeat != null) o["repeat"] = RuleNode(n.Repeat);
            if (!string.IsNullOrEmpty(n.Series)) o["series"] = n.Series;
            return o;
        }

        private static JsonObject RuleNode(RepeatRule r)
        {
            var o = new JsonObject
            {
                ["freq"] = r.Freq, ["every"] = r.Every,
                ["days"] = new JsonArray((r.Days ?? new List<int>()).Select(d => (JsonNode)JsonValue.Create(d)).ToArray()),
                ["by"] = r.By, ["nth"] = r.Nth, ["wd"] = r.Wd, ["shiftUnit"] = r.ShiftUnit ?? "", ["shift"] = r.Shift,
                ["shiftWd"] = r.ShiftWd, ["until"] = r.Until ?? ""
            };
            if (r.Skip != null) o["skip"] = new JsonArray(r.Skip.Select(d => (JsonNode)JsonValue.Create(d)).ToArray());
            return o;
        }

        private static long Num(JsonNode n)
        {
            try
            {
                if (n is JsonValue v)
                {
                    if (v.TryGetValue(out long l)) return l;
                    if (v.TryGetValue(out double d)) return (long)d;
                    if (v.TryGetValue(out string s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p)) return (long)p;
                }
            }
            catch (Exception) { /* not a number */ }
            return 0;
        }

        private static string Str(JsonNode n)
        {
            try { return n is JsonValue v && v.TryGetValue(out string s) ? s : null; }
            catch (Exception) { return null; }
        }

        /// <summary>A note from notes.json, checked: only what QuickCal itself writes. Null when there is nothing to keep.</summary>
        private static Note ReadShared(string k, JsonNode node)
        {
            if (!(node is JsonObject o)) return null;
            var n = new Note { Date = k, Text = Str(o["text"]) ?? "", Color = Str(o["color"]) ?? "", Updated = Num(o["updated"]) };
            if (NoteColors.Hex(n.Color) == null) n.Color = "";
            if (o["images"] is JsonArray imgs) n.Images = imgs.Select(Str).Where(NoteStore.IsPictureName).Take(NoteStore.MaxImages).ToList();
            string end = Str(o["endDate"]);
            if (Keys.IsDayKey(end)) n.EndDate = end;
            string series = Str(o["series"]);
            if (Keys.IsNoteKey(series)) n.Series = series;
            if (o["repeat"] is JsonObject r)
            {
                try
                {
                    n.Repeat = JsonSerializer.Deserialize<RepeatRule>(r.ToJsonString()) ?? null;
                    if (n.Repeat != null)
                    {
                        n.Repeat.Freq = new[] { "day", "week", "month", "year" }.Contains(n.Repeat.Freq) ? n.Repeat.Freq : "week";
                        n.Repeat.Days ??= new List<int>();
                        n.Repeat.Until = Keys.IsDayKey(n.Repeat.Until) ? n.Repeat.Until : "";
                        n.Repeat.ShiftUnit ??= "";
                    }
                }
                catch (Exception)
                {
                    n.Repeat = null;
                }
            }
            if (string.IsNullOrWhiteSpace(n.Text) && n.Images.Count == 0 && n.Color == "" && n.Repeat == null) return null;
            return n;
        }

        private static JsonObject Sorted<TV>(IDictionary<string, TV> d, Func<TV, JsonNode> make)
        {
            var o = new JsonObject();
            foreach (string k in d.Keys.OrderBy(x => x, StringComparer.Ordinal)) o[k] = make(d[k]);
            return o;
        }

        private async Task SyncOnce(int attempt)
        {
            try
            {
                await SyncOnceInner();
            }
            catch (DropboxException e) when (e.Summary.Contains("conflict") && attempt < 2)
            {
                // Another device wrote notes.json in between: start over with its version
                await SyncOnce(attempt + 1);
            }
        }

        private async Task SyncOnceInner()
        {
            DropboxClient D = Dropbox;
            JsonObject remote = null;
            string rev = "";
            var rNotes = new Dictionary<string, Note>();
            var rDeleted = new Dictionary<string, long>();

            var f = await D.Download(SyncFile);
            if (f != null)
            {
                rev = f.Value.Rev;
                try { remote = JsonNode.Parse(Encoding.UTF8.GetString(f.Value.Bytes)) as JsonObject; } catch (Exception) { remote = null; }
                if (remote != null && Str(remote["kind"]) != SyncKind) remote = null;
                // Written by a newer QuickCal: leave it alone rather than write it back in the old way
                if (remote != null && Num(remote["version"]) > 1) throw new DropboxException("newer sync file") { Newer = true };
                if (remote != null)
                {
                    if (remote["notes"] is JsonObject ns)
                        foreach (var p in ns)
                        {
                            if (!Keys.IsNoteKey(p.Key)) continue;
                            Note n = ReadShared(p.Key, p.Value);
                            if (n != null) rNotes[p.Key] = n;
                        }
                    if (remote["deleted"] is JsonObject ds)
                        foreach (var p in ds) rDeleted[p.Key] = Num(p.Value);
                }
            }

            // Delete marks from the other devices
            foreach (var p in rDeleted)
                if (!(store.Deleted.TryGetValue(p.Key, out long t) && t >= p.Value)) store.Deleted[p.Key] = p.Value;
            store.CleanMarks();

            // How long notes are kept: the latest change wins, before anything is judged too old
            if (remote != null && remote["keepWeeks"] != null && Num(remote["keepUpdated"]) > store.KeepUpdated)
            {
                bool shorter = NotesEngine.CleanWeeks(Num(remote["keepWeeks"])) < store.KeepWeeks;
                store.SaveKeep((int)Num(remote["keepWeeks"]), fromSync: true, time: Num(remote["keepUpdated"]));
                if (shorter) KeepShortened?.Invoke();   // set shorter on another device: the older notes go here too
            }

            var local = store.Notes.ToDictionary(p => p.Key, p => p.Value);
            var result = new Dictionary<string, Note>();
            var toPut = new List<Note>();
            var toDelete = new List<string>();
            string limit = NotesEngine.OldestKept(store.KeepWeeks);
            foreach (string k in local.Keys.Concat(rNotes.Keys).Distinct())
            {
                local.TryGetValue(k, out Note l);
                rNotes.TryGetValue(k, out Note r);
                Note best = l == null ? r : r == null ? l : r.Updated > l.Updated ? r : l;
                if (store.Deleted.TryGetValue(k, out long del) && del > best.Updated)
                {
                    if (l != null) toDelete.Add(k);
                    continue;
                }
                if (NotesEngine.Expired(k, best.EndDate, best.Repeat, limit)) continue;   // deleted here soon anyway
                if (best != l) toPut.Add(best);
                result[k] = best;
            }

            // Color names: the latest change wins
            if (remote != null && remote["colorNames"] is JsonObject cn && Num(remote["namesUpdated"]) > store.NamesUpdated)
            {
                store.ColorNames.Clear();
                foreach (var p in cn)
                {
                    string v = Str(p.Value);
                    if (v != null) store.ColorNames[p.Key] = v;
                }
                store.NamesUpdated = Num(remote["namesUpdated"]);
                store.SaveNames(fromSync: true);
                NotesChanged?.Invoke();
            }

            // 1. Pictures this PC does not have yet (also ones that could not be fetched last time), then the
            //    notes from Dropbox into this PC. A picture still missing keeps its name, so it is never lost for the others.
            bool gotPictures = false;
            foreach (string p in result.Values.SelectMany(n => n.Images).Distinct().Where(p => !store.HasPicture(p)).ToList())
            {
                var pic = await D.Download("/img/" + p);
                if (pic != null) { store.WritePicture(p, pic.Value.Bytes); gotPictures = true; }
            }
            if (toPut.Count > 0 || toDelete.Count > 0) store.Apply(toPut, toDelete);
            if (toPut.Count > 0 || toDelete.Count > 0 || gotPictures) NotesChanged?.Invoke();

            // 2. Our pictures Dropbox does not have, then notes.json (only if something changed)
            var had = new HashSet<string>(rNotes.Values.SelectMany(n => n.Images));
            var file = new JsonObject
            {
                ["kind"] = SyncKind,
                ["version"] = 1,
                ["notes"] = Sorted(result, Shared),
                ["deleted"] = Sorted(store.Deleted, v => JsonValue.Create(v)),
                ["colorNames"] = new JsonObject(store.ColorNames.Select(p => new KeyValuePair<string, JsonNode>(p.Key, JsonValue.Create(p.Value)))),
                ["namesUpdated"] = store.NamesUpdated,
                ["keepWeeks"] = store.KeepWeeks,
                ["keepUpdated"] = store.KeepUpdated
            };
            if (remote != null && SameAsRemote(file, remote)) return;

            var uses = new HashSet<string>(result.Values.SelectMany(n => n.Images));
            foreach (string p in uses.Where(p => !had.Contains(p)).ToList())
            {
                byte[] bytes = store.ReadPicture(p);
                if (bytes != null) await D.Upload("/img/" + p, bytes, null);
            }
            byte[] json = Encoding.UTF8.GetBytes(file.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            await D.Upload(SyncFile, json, rev);
            // Pictures no note uses any more
            foreach (string p in had.Where(p => !uses.Contains(p)).ToList()) await D.Remove("/img/" + p);
        }

        /// <summary>The file we would write says the same as the one in Dropbox (then nothing is sent).</summary>
        private static bool SameAsRemote(JsonObject file, JsonObject remote)
        {
            var copy = new JsonObject();
            foreach (string k in new[] { "kind", "version", "notes", "deleted", "colorNames", "namesUpdated", "keepWeeks", "keepUpdated" })
                copy[k] = remote[k]?.DeepClone();
            // Compare the notes as they were read (checked), the way they would be written
            if (remote["notes"] is JsonObject ns)
            {
                var checkedNotes = new JsonObject();
                foreach (var p in ns.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!Keys.IsNoteKey(p.Key)) continue;
                    Note n = ReadShared(p.Key, p.Value);
                    if (n != null) checkedNotes[p.Key] = Shared(n);
                }
                copy["notes"] = checkedNotes;
            }
            return JsonNode.DeepEquals(file, copy);
        }
    }
}
