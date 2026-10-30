using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickCal.Notes
{
    /// <summary>
    /// Keeps the notes on disk, next to QuickCal.exe like the settings, so they travel with the app:
    /// QuickCal.notes.json (every note, the color names and how long notes are kept) and the folder
    /// QuickCal.notes-pictures (one file per picture, named by its content, as in Dropbox).
    /// If that folder is read-only, %AppData%\QuickCal is used instead.
    /// </summary>
    public sealed class NoteStore
    {
        public const string FileName = "QuickCal.notes.json";
        public const string PicturesFolderName = "QuickCal.notes-pictures";
        public const int KeepMarksDays = 120;
        public const int MaxImages = 4, MaxImageSide = 1600;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>What is written to QuickCal.notes.json.</summary>
        private sealed class FileData
        {
            [JsonPropertyName("kind")] public string Kind { get; set; } = "quickcal-notes-windows";
            [JsonPropertyName("version")] public int Version { get; set; } = 1;
            [JsonPropertyName("notes")] public Dictionary<string, Note> Notes { get; set; } = new Dictionary<string, Note>();
            /// <summary>Deleted notes: key -> when, so a delete reaches the other devices that sync with Dropbox.</summary>
            [JsonPropertyName("deleted")] public Dictionary<string, long> Deleted { get; set; } = new Dictionary<string, long>();
            [JsonPropertyName("colorNames")] public Dictionary<string, string> ColorNames { get; set; } = new Dictionary<string, string>();
            [JsonPropertyName("namesUpdated")] public long NamesUpdated { get; set; }
            [JsonPropertyName("keepWeeks")] public int KeepWeeks { get; set; } = NotesEngine.KeepDefault;
            [JsonPropertyName("keepUpdated")] public long KeepUpdated { get; set; }
            [JsonPropertyName("lastBackup")] public string LastBackup { get; set; } = "";
            [JsonPropertyName("lastSynced")] public long LastSynced { get; set; }
            /// <summary>The Dropbox sign-in, encrypted for this Windows user (DPAPI).</summary>
            [JsonPropertyName("dropbox")] public string Dropbox { get; set; }
        }

        private FileData data = new FileData();
        private string folder;

        public Dictionary<string, Note> Notes => data.Notes;
        public Dictionary<string, long> Deleted => data.Deleted;
        public Dictionary<string, string> ColorNames => data.ColorNames;
        public long NamesUpdated { get => data.NamesUpdated; set => data.NamesUpdated = value; }
        public int KeepWeeks { get => data.KeepWeeks; set => data.KeepWeeks = NotesEngine.CleanWeeks(value); }
        public long KeepUpdated { get => data.KeepUpdated; set => data.KeepUpdated = value; }
        public string LastBackup { get => data.LastBackup ?? ""; set => data.LastBackup = value; }
        public long LastSynced { get => data.LastSynced; set => data.LastSynced = value; }
        public string DropboxAuth { get => data.Dropbox; set => data.Dropbox = value; }

        /// <summary>The notes could be read (false: QuickCal.notes.json is damaged; it is then left alone).</summary>
        public bool Available { get; private set; } = true;

        /// <summary>Something changed that other devices should get (not set while writing what came from Dropbox).</summary>
        public event Action Changed;

        public string FilePath => Path.Combine(folder, FileName);
        public string PicturesFolder => Path.Combine(folder, PicturesFolderName);

        private static string ExeFolder => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        private static string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickCal");

        public void Load()
        {
            folder = ChooseFolder();
            if (!File.Exists(FilePath)) return;
            try
            {
                var loaded = JsonSerializer.Deserialize<FileData>(File.ReadAllText(FilePath), JsonOptions) ?? new FileData();
                data = loaded;
                data.Notes ??= new Dictionary<string, Note>();
                data.Deleted ??= new Dictionary<string, long>();
                data.ColorNames ??= new Dictionary<string, string>();
                data.KeepWeeks = NotesEngine.CleanWeeks(data.KeepWeeks);
                foreach (string k in data.Notes.Keys.ToList())
                {
                    Note n = data.Notes[k];
                    if (!Keys.IsNoteKey(k) || n == null) { data.Notes.Remove(k); continue; }
                    n.Date = k;
                    n.Text ??= "";
                    n.Color = NoteColors.Hex(n.Color) != null ? n.Color : "";
                    n.Images = (n.Images ?? new List<string>()).Where(IsPictureName).ToList();
                }
            }
            catch (Exception)
            {
                // Damaged: keep the file as it is (never overwrite it) and run without notes
                Available = false;
            }
        }

        /// <summary>Next to QuickCal.exe when notes are already there or the folder can be written to, else %AppData%\QuickCal.</summary>
        private static string ChooseFolder()
        {
            if (File.Exists(Path.Combine(ExeFolder, FileName))) return ExeFolder;
            if (File.Exists(Path.Combine(AppDataFolder, FileName))) return AppDataFolder;
            try
            {
                string probe = Path.Combine(ExeFolder, ".quickcal-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return ExeFolder;
            }
            catch (Exception)
            {
                return AppDataFolder;
            }
        }

        public void Save()
        {
            if (!Available || folder == null) return;
            string json = JsonSerializer.Serialize(data, JsonOptions);
            try
            {
                Directory.CreateDirectory(folder);
                // Write a new file first, then swap it in, so a crash halfway never leaves a half-written file
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception)
            {
                // Nowhere to save right now; the next change tries again.
            }
        }

        // =====================================================================================
        // Notes
        // =====================================================================================

        public Note Get(string k) => data.Notes.TryGetValue(k, out Note n) ? n : null;

        /// <summary>Save a note (or delete it when nothing is left in it). Marks it as changed now.</summary>
        public void Put(Note n, bool keepTime = false)
        {
            if (n.IsEmpty) { Delete(n.Date); return; }
            if (!keepTime) n.Updated = Keys.Now();
            data.Notes[n.Date] = n;
            Save();
            Changed?.Invoke();
        }

        public void Delete(string k)
        {
            DeleteMany(new[] { k });
        }

        /// <summary>Delete notes and leave a mark (which and when), so the delete reaches the other devices.</summary>
        public void DeleteMany(IEnumerable<string> keys)
        {
            long now = Keys.Now();
            foreach (string k in keys.ToList())
            {
                data.Notes.Remove(k);
                data.Deleted[k] = now;
            }
            CleanMarks();
            Save();
            Changed?.Invoke();
        }

        /// <summary>Put notes back (null = no note there), newer than the change they undo, also on other devices.</summary>
        public void PutBack(IList<string> keys, IList<Note> records)
        {
            long now = Keys.Now();
            for (int i = 0; i < keys.Count; i++)
            {
                if (records[i] != null)
                {
                    Note r = records[i].Copy();
                    r.Updated = now;
                    data.Notes[keys[i]] = r;
                }
                else
                {
                    data.Notes.Remove(keys[i]);
                    data.Deleted[keys[i]] = now;
                }
            }
            Save();
            Changed?.Invoke();
        }

        /// <summary>Notes from Dropbox: written as they are, not a change to send back.</summary>
        public void Apply(IEnumerable<Note> put, IEnumerable<string> delete)
        {
            foreach (Note n in put) data.Notes[n.Date] = n;
            foreach (string k in delete) data.Notes.Remove(k);
            Save();
        }

        /// <summary>Restore from a backup: notes in the file replace notes that start on the same day.</summary>
        public void PutMany(IEnumerable<Note> notes)
        {
            foreach (Note n in notes) data.Notes[n.Date] = n;
            Save();
            Changed?.Invoke();
        }

        public void CleanMarks()
        {
            long old = Keys.Now() - KeepMarksDays * 86400000L;
            foreach (string k in data.Deleted.Where(p => p.Value < old).Select(p => p.Key).ToList()) data.Deleted.Remove(k);
        }

        public void SaveNames(bool fromSync = false)
        {
            if (!fromSync) data.NamesUpdated = Keys.Now();
            Save();
            if (!fromSync) Changed?.Invoke();
        }

        public void SaveKeep(int weeks, bool fromSync = false, long time = 0)
        {
            data.KeepWeeks = NotesEngine.CleanWeeks(weeks);
            data.KeepUpdated = time != 0 ? time : Keys.Now();
            Save();
            if (!fromSync) Changed?.Invoke();
        }

        /// <summary>Delete the notes that ended before the first day kept. Returns how many were deleted.</summary>
        public int RemoveOld()
        {
            string limit = NotesEngine.OldestKept(KeepWeeks);
            var old = data.Notes.Values.Where(n => NotesEngine.Expired(n.Date, n.EndDate, n.Repeat, limit)).Select(n => n.Date).ToList();
            if (old.Count == 0) return 0;
            // Too old everywhere: no delete marks needed, the other devices delete them by themselves
            foreach (string k in old) data.Notes.Remove(k);
            Save();
            return old.Count;
        }

        // =====================================================================================
        // Pictures: one file per picture, named by the SHA-256 of its content (+ .jpg or .png)
        // =====================================================================================

        public static bool IsPictureName(string p) =>
            p != null && p.Length == 68 && (p.EndsWith(".jpg", StringComparison.Ordinal) || p.EndsWith(".png", StringComparison.Ordinal)) &&
            p.Take(64).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

        public static string PictureName(byte[] bytes, bool png) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + (png ? ".png" : ".jpg");

        /// <summary>Keeps the picture and gives its name.</summary>
        public string AddPicture(byte[] bytes, bool png)
        {
            string name = PictureName(bytes, png);
            WritePicture(name, bytes);
            return name;
        }

        public void WritePicture(string name, byte[] bytes)
        {
            string path = Path.Combine(PicturesFolder, name);
            if (File.Exists(path)) return;
            Directory.CreateDirectory(PicturesFolder);
            File.WriteAllBytes(path, bytes);
        }

        public bool HasPicture(string name) => IsPictureName(name) && File.Exists(Path.Combine(PicturesFolder, name));

        public byte[] ReadPicture(string name)
        {
            try
            {
                return IsPictureName(name) ? File.ReadAllBytes(Path.Combine(PicturesFolder, name)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Pictures no note uses any more (only at startup, so Undo can still bring a deleted note's pictures back).</summary>
        public void RemoveUnusedPictures()
        {
            try
            {
                if (!Available || !Directory.Exists(PicturesFolder)) return;
                var used = new HashSet<string>(data.Notes.Values.SelectMany(n => n.Images));
                foreach (string path in Directory.GetFiles(PicturesFolder))
                {
                    string name = Path.GetFileName(path);
                    if (IsPictureName(name) && !used.Contains(name)) File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Only tidying up
            }
        }
    }
}
