using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace QuickCal.Notes
{
    /// <summary>A failed Dropbox call, with what kind of failure it was.</summary>
    public sealed class DropboxException : Exception
    {
        public string Summary { get; }
        public bool Offline { get; set; }
        public bool SignedOut { get; set; }
        public bool Busy { get; set; }
        public bool Newer { get; set; }

        public DropboxException(string message, string summary = "") : base(message) { Summary = summary ?? ""; }
    }

    /// <summary>
    /// Sign in to Dropbox, and read and write files in QuickCal's own folder there. The same Dropbox app as the
    /// web app (Apps/QuickCal), so the notes are shared with QuickCal on the web and on the phone.
    /// Sign-in is OAuth with PKCE (no secret): Dropbox opens in the browser and shows a code, which the user
    /// pastes into QuickCal. The refresh token from Dropbox does not expire, so the user stays connected until
    /// they disconnect. It is kept encrypted for this Windows user (DPAPI), in QuickCal.notes.json.
    /// </summary>
    public sealed class DropboxClient
    {
        public const string AppKey = "x35z8c3bhus4jyg";
        public const string Folder = "Apps/QuickCal";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        private sealed class Auth
        {
            [JsonPropertyName("refresh")] public string Refresh { get; set; }
            [JsonPropertyName("access")] public string Access { get; set; }
            [JsonPropertyName("expires")] public long Expires { get; set; }
            [JsonPropertyName("name")] public string Name { get; set; }
            [JsonPropertyName("email")] public string Email { get; set; }
            /// <summary>Was connected, and Dropbox stopped accepting the sign-in (account deleted, access removed).</summary>
            [JsonPropertyName("lost")] public bool Lost { get; set; }
        }

        private static readonly HttpClient Http = new HttpClient { Timeout = Timeout };

        private readonly NoteStore store;
        private Auth auth;
        private string verifier;

        public DropboxClient(NoteStore store)
        {
            this.store = store;
            auth = ReadAuth();
        }

        public bool Connected => auth?.Refresh != null;
        public bool Lost => auth != null && auth.Refresh == null && auth.Lost;
        public string AccountName => auth?.Name ?? "";
        public string AccountEmail => auth?.Email ?? "";

        private Auth ReadAuth()
        {
            try
            {
                if (string.IsNullOrEmpty(store.DropboxAuth)) return null;
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(store.DropboxAuth), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<Auth>(plain);
            }
            catch (Exception)
            {
                // Another Windows user or another PC (QuickCal.exe moved with its files): connect again there
                return null;
            }
        }

        private void SaveAuth()
        {
            try
            {
                store.DropboxAuth = auth == null ? null
                    : Convert.ToBase64String(ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(auth), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                store.DropboxAuth = null;
            }
            store.Save();
        }

        private static string B64Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static string Query(IDictionary<string, string> o) =>
            string.Join("&", o.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

        // ---------- Sign in ----------

        /// <summary>Opens Dropbox's sign-in page in the browser. Dropbox then shows a code to paste into QuickCal.</summary>
        public void StartConnect()
        {
            verifier = B64Url(RandomNumberGenerator.GetBytes(48));
            string challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string url = "https://www.dropbox.com/oauth2/authorize?" + Query(new Dictionary<string, string>
            {
                ["client_id"] = AppKey, ["response_type"] = "code", ["token_access_type"] = "offline",
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
            });
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        public bool WaitingForCode => verifier != null;

        /// <summary>The code Dropbox showed after signing in.</summary>
        public async Task FinishConnect(string code)
        {
            if (verifier == null) throw new DropboxException("not started");
            await Token(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code.Trim(), ["code_verifier"] = verifier
            }).ConfigureAwait(true);
            verifier = null;
        }

        public void CancelConnect() => verifier = null;

        private async Task Token(Dictionary<string, string> p)
        {
            p["client_id"] = AppKey;
            HttpResponseMessage r = await Send(() => new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/oauth2/token")
            {
                Content = new StringContent(Query(p), Encoding.UTF8, "application/x-www-form-urlencoded")
            }).ConfigureAwait(true);
            string body = await r.Content.ReadAsStringAsync().ConfigureAwait(true);
            JsonElement j = default;
            try { j = JsonDocument.Parse(body).RootElement; } catch (Exception) { /* not JSON */ }
            string Str(string name) => j.ValueKind == JsonValueKind.Object && j.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string access = Str("access_token");
            if (!r.IsSuccessStatusCode || access == null)
            {
                string error = Str("error");
                throw new DropboxException(Str("error_description") ?? error ?? "sign-in failed") { SignedOut = error == "invalid_grant" };
            }
            long expiresIn = j.TryGetProperty("expires_in", out JsonElement e) && e.TryGetInt64(out long x) ? x : 3600;
            auth = new Auth
            {
                Refresh = Str("refresh_token") ?? auth?.Refresh, Access = access, Expires = Keys.Now() + expiresIn * 1000,
                Name = auth?.Name, Email = auth?.Email
            };
            SaveAuth();
            if (p["grant_type"] == "authorization_code")
            {
                try
                {
                    using JsonDocument a = await Rpc("users/get_current_account", null).ConfigureAwait(true);
                    if (a.RootElement.TryGetProperty("name", out JsonElement n) && n.TryGetProperty("display_name", out JsonElement dn)) auth.Name = dn.GetString();
                    if (a.RootElement.TryGetProperty("email", out JsonElement em)) auth.Email = em.GetString();
                    SaveAuth();
                }
                catch (Exception)
                {
                    // The name is only for show
                }
            }
        }

        /// <summary>A valid access token (they last a few hours), fetched again with the refresh token when needed.</summary>
        private async Task<string> Access()
        {
            if (auth?.Refresh == null) throw SignedOutError();
            if (auth.Access != null && auth.Expires > Keys.Now() + 60000) return auth.Access;
            try
            {
                await Token(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = auth.Refresh }).ConfigureAwait(true);
                return auth.Access;
            }
            catch (DropboxException e) when (e.SignedOut)
            {
                // Dropbox says no to the sign-in: the account is deleted, or the user removed QuickCal's access in
                // Dropbox. Forget it and remember why, so QuickCal can say so. The notes here are not touched.
                auth = new Auth { Lost = true };
                SaveAuth();
                throw SignedOutError();
            }
        }

        private static DropboxException SignedOutError() => new DropboxException("not connected to Dropbox") { SignedOut = true };

        public async Task Disconnect()
        {
            if (auth?.Access != null)
            {
                try { (await Rpc("auth/token/revoke", null).ConfigureAwait(true)).Dispose(); } catch (Exception) { /* forgotten here anyway */ }
            }
            auth = null;
            SaveAuth();
        }

        // ---------- Calls ----------

        /// <summary>Sends a request; a network failure (offline, Dropbox not reachable, no answer) gets Offline = true.</summary>
        private static async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> make)
        {
            try
            {
                using var cts = new CancellationTokenSource(Timeout);
                return await Http.SendAsync(make(), cts.Token).ConfigureAwait(true);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is OperationCanceledException)
            {
                throw new DropboxException(e is HttpRequestException ? "no network" : "Dropbox did not answer") { Offline = true };
            }
        }

        /// <summary>Dropbox wants its arguments in a header for file contents: only ASCII there.</summary>
        private static string ArgHeader(object o)
        {
            string json = JsonSerializer.Serialize(o);
            var sb = new StringBuilder();
            foreach (char c in json) sb.Append(c > 0x7e ? "\\u" + ((int)c).ToString("x4") : c.ToString());
            return sb.ToString();
        }

        /// <summary>One call, signed in. Tries again once with a new access token if Dropbox says the old one is no good.</summary>
        private async Task<HttpResponseMessage> Call(Func<HttpRequestMessage> make, bool again = false)
        {
            string token = await Access().ConfigureAwait(true);
            HttpResponseMessage r = await Send(() =>
            {
                HttpRequestMessage m = make();
                m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return m;
            }).ConfigureAwait(true);
            if ((int)r.StatusCode == 401 && !again && auth != null)
            {
                auth.Access = null;
                return await Call(make, true).ConfigureAwait(true);
            }
            if ((int)r.StatusCode == 409)
            {
                // An error from the call itself, like a file that is not there
                string summary = "";
                try
                {
                    using JsonDocument j = JsonDocument.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(true));
                    if (j.RootElement.TryGetProperty("error_summary", out JsonElement s)) summary = s.GetString() ?? "";
                }
                catch (Exception) { /* no details */ }
                throw new DropboxException(summary == "" ? "Dropbox error" : summary, summary);
            }
            if (!r.IsSuccessStatusCode)
            {
                int code = (int)r.StatusCode;
                throw new DropboxException("Dropbox " + code) { Busy = code == 429 || code >= 500 };
            }
            return r;
        }

        private async Task<JsonDocument> Rpc(string endpoint, object args)
        {
            HttpResponseMessage r = await Call(() => new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/" + endpoint)
            {
                Content = new StringContent(args == null ? "null" : JsonSerializer.Serialize(args), Encoding.UTF8, "application/json")
            }).ConfigureAwait(true);
            return JsonDocument.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(true));
        }

        /// <summary>The file and its version (rev), or null when the file is not there.</summary>
        public async Task<(byte[] Bytes, string Rev)?> Download(string path)
        {
            try
            {
                HttpResponseMessage r = await Call(() =>
                {
                    var m = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/download");
                    m.Headers.TryAddWithoutValidation("Dropbox-API-Arg", ArgHeader(new { path }));
                    return m;
                }).ConfigureAwait(true);
                string rev = "";
                if (r.Headers.TryGetValues("Dropbox-API-Result", out IEnumerable<string> values))
                {
                    try
                    {
                        using JsonDocument meta = JsonDocument.Parse(values.First());
                        if (meta.RootElement.TryGetProperty("rev", out JsonElement v)) rev = v.GetString() ?? "";
                    }
                    catch (Exception) { /* no rev: treated as new */ }
                }
                return (await r.Content.ReadAsByteArrayAsync().ConfigureAwait(true), rev);
            }
            catch (DropboxException e) when (e.Summary.Contains("not_found"))
            {
                return null;
            }
        }

        /// <summary>rev: replace only that version (else a conflict error), "" = a new file, null = overwrite.</summary>
        public async Task Upload(string path, byte[] bytes, string rev)
        {
            object mode = rev == null ? "overwrite" : rev != "" ? (object)new Dictionary<string, string> { [".tag"] = "update", ["update"] = rev } : "add";
            (await Call(() =>
            {
                var m = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload") { Content = new ByteArrayContent(bytes) };
                m.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                m.Headers.TryAddWithoutValidation("Dropbox-API-Arg", ArgHeader(new Dictionary<string, object> { ["path"] = path, ["mode"] = mode, ["mute"] = true, ["autorename"] = false }));
                return m;
            }).ConfigureAwait(true)).Dispose();
        }

        public async Task Remove(string path)
        {
            try
            {
                (await Rpc("files/delete_v2", new { path }).ConfigureAwait(true)).Dispose();
            }
            catch (DropboxException e) when (e.Summary.Contains("not_found"))
            {
                // Already gone
            }
        }
    }
}
