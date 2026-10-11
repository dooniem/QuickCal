/* QuickCal Web (beta) - Dropbox: sign in, and read and write files in QuickCal's own folder in the user's Dropbox.
   There is no server of ours: the browser talks to Dropbox directly. Sign-in is OAuth with PKCE (no secret),
   and the refresh token from Dropbox does not expire, so the user stays connected until they disconnect.
   QuickCal only gets its own folder (an "App folder" app), never the rest of the user's Dropbox.
   Used by notes.js (the sync) and app.js (the Dropbox page). */
window.QuickCalDropbox = (function () {
  'use strict';

  // The app key from https://www.dropbox.com/developers/apps (it is not a secret). Empty: Dropbox is not set up yet.
  var APP_KEY = '';
  // The folder Dropbox gives the app, as the user sees it in their Dropbox
  var FOLDER = 'Apps/QuickCal';

  var AUTH_KEY = 'quickcal-beta.dropbox';            // { refresh, access, expires, name, email }
  var PENDING_KEY = 'quickcal-beta.dropboxPending';  // { redirect: {verifier, state, uri}, code: {verifier} } while signing in
  var auth = read(AUTH_KEY);

  function read(k) { try { return JSON.parse(localStorage.getItem(k)) || null; } catch (e) { return null; } }
  function write(k, v) { try { if (v) localStorage.setItem(k, JSON.stringify(v)); else localStorage.removeItem(k); } catch (e) { /* storage blocked */ } }
  function saveAuth() { write(AUTH_KEY, auth); }

  function b64url(buf) {
    var bytes = new Uint8Array(buf), s = '';
    for (var i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
  function random(n) { var a = new Uint8Array(n); crypto.getRandomValues(a); return b64url(a); }
  function query(o) {
    return Object.keys(o).filter(function (k) { return o[k] != null && o[k] !== ''; })
      .map(function (k) { return encodeURIComponent(k) + '=' + encodeURIComponent(o[k]); }).join('&');
  }
  // Back to this page after signing in (the address must be added under Redirect URIs in the Dropbox app)
  function redirectUri() { return location.origin + location.pathname.replace(/index\.html$/, ''); }

  // ---------- Sign in ----------
  // withRedirect: Dropbox sends the user back here. Without: Dropbox shows a code to paste into QuickCal,
  // for a phone where the sign-in opens outside the app and cannot come back to it.
  function authorizeUrl(withRedirect) {
    var verifier = random(48), state = random(12);
    var pending = read(PENDING_KEY) || {};
    pending[withRedirect ? 'redirect' : 'code'] = { verifier: verifier, state: state, uri: withRedirect ? redirectUri() : '' };
    write(PENDING_KEY, pending);
    return crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier)).then(function (h) {
      return 'https://www.dropbox.com/oauth2/authorize?' + query({
        client_id: APP_KEY, response_type: 'code', token_access_type: 'offline',
        code_challenge: b64url(h), code_challenge_method: 'S256',
        redirect_uri: withRedirect ? redirectUri() : '', state: withRedirect ? state : ''
      });
    });
  }
  function connect() { return authorizeUrl(true).then(function (url) { location.href = url; }); }
  function codeUrl() { return authorizeUrl(false); }

  // This page was opened by Dropbox after signing in (or after the user said no)
  function isReturn() { return /[?&](code|error)=/.test(location.search) && !!(read(PENDING_KEY) || {}).redirect; }
  function finishRedirect() {
    var p = new URLSearchParams(location.search), pending = read(PENDING_KEY) || {}, r = pending.redirect;
    history.replaceState(null, '', redirectUri());   // the code must not stay in the address
    delete pending.redirect; write(PENDING_KEY, pending);
    if (!r || !p.get('code') || p.get('state') !== r.state) return Promise.reject(new Error(p.get('error') || 'no code'));
    return token({ grant_type: 'authorization_code', code: p.get('code'), code_verifier: r.verifier, redirect_uri: r.uri });
  }
  function finishCode(code) {
    var pending = read(PENDING_KEY) || {}, c = pending.code;
    if (!c || !code.trim()) return Promise.reject(new Error('no code'));
    return token({ grant_type: 'authorization_code', code: code.trim(), code_verifier: c.verifier }).then(function () {
      delete pending.code; write(PENDING_KEY, pending);
    });
  }
  // Signed in: keep the tokens and the name to show
  function token(params) {
    params.client_id = APP_KEY;
    return fetch('https://api.dropboxapi.com/oauth2/token', {
      method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: query(params)
    }).then(function (r) {
      return r.json().catch(function () { return {}; }).then(function (j) {
        if (!r.ok || !j.access_token) { var e = new Error(j.error_description || j.error || 'sign-in failed'); e.signedOut = j.error === 'invalid_grant'; throw e; }
        return j;
      });
    }).then(function (j) {
      auth = { refresh: j.refresh_token || (auth && auth.refresh), access: j.access_token, expires: Date.now() + (j.expires_in || 3600) * 1000,
               name: auth && auth.name, email: auth && auth.email };
      saveAuth();
      if (params.grant_type === 'authorization_code') {
        return rpc('users/get_current_account', null).then(function (a) {
          auth.name = a.name && a.name.display_name; auth.email = a.email; saveAuth();
        }, function () { /* the name is only for show */ });
      }
    });
  }
  // A valid access token (they last a few hours), fetched again with the refresh token when needed
  function access() {
    if (!auth || !auth.refresh) return Promise.reject(signedOut());
    if (auth.access && auth.expires > Date.now() + 60000) return Promise.resolve(auth.access);
    return token({ grant_type: 'refresh_token', refresh_token: auth.refresh }).then(function () { return auth.access; }, function (e) {
      if (e.signedOut) { auth = null; saveAuth(); throw signedOut(); }   // access removed in Dropbox
      throw e;
    });
  }
  function signedOut() { var e = new Error('not connected to Dropbox'); e.signedOut = true; return e; }
  function disconnect() {
    var done = auth && auth.access ? rpc('auth/token/revoke', null).catch(function () {}) : Promise.resolve();
    return done.then(function () { auth = null; saveAuth(); });
  }

  // ---------- Files ----------
  // Dropbox wants its arguments in a header for file contents: only ASCII there
  function argHeader(o) {
    return JSON.stringify(o).replace(/[\u007f-￿]/g, function (c) { return '\\u' + ('000' + c.charCodeAt(0).toString(16)).slice(-4); });
  }
  // One call, signed in. Tries again once with a new access token if Dropbox says the old one is no good.
  function call(url, init, again) {
    return access().then(function (t) {
      init.headers.Authorization = 'Bearer ' + t;
      return fetch(url, init);
    }).then(function (r) {
      if (r.status === 401 && !again && auth) { auth.access = null; return call(url, init, true); }
      if (r.status === 409) {   // an error from the call itself, like a file that is not there
        return r.json().catch(function () { return {}; }).then(function (j) {
          var e = new Error(j.error_summary || 'Dropbox error'); e.summary = j.error_summary || ''; throw e;
        });
      }
      if (!r.ok) throw new Error('Dropbox ' + r.status);
      return r;
    });
  }
  function rpc(endpoint, args) {
    return call('https://api.dropboxapi.com/2/' + endpoint, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(args)
    }).then(function (r) { return r.json(); });
  }
  /** { blob, rev } or null when the file is not there */
  function download(path) {
    return call('https://content.dropboxapi.com/2/files/download', {
      method: 'POST', headers: { 'Dropbox-API-Arg': argHeader({ path: path }) }
    }).then(function (r) {
      var meta = {};
      try { meta = JSON.parse(r.headers.get('Dropbox-API-Result')) || {}; } catch (e) { /* no rev: treated as new */ }
      return r.blob().then(function (b) { return { blob: b, rev: meta.rev || '' }; });
    }, function (e) {
      if (/not_found/.test(e.summary || '')) return null;
      throw e;
    });
  }
  /** rev: replace only that version (else a conflict error), '' = a new file, null = overwrite. Gives the new rev. */
  function upload(path, blob, rev) {
    var mode = rev === null ? 'overwrite' : rev ? { '.tag': 'update', update: rev } : 'add';
    return call('https://content.dropboxapi.com/2/files/upload', {
      method: 'POST',
      headers: { 'Content-Type': 'application/octet-stream', 'Dropbox-API-Arg': argHeader({ path: path, mode: mode, mute: true, autorename: false }) },
      body: blob
    }).then(function (r) { return r.json(); }).then(function (m) { return m.rev; });
  }
  function remove(path) {
    return rpc('files/delete_v2', { path: path }).catch(function (e) { if (!/not_found/.test(e.summary || '')) throw e; });
  }

  return {
    ready: function () { return !!APP_KEY; },     // set up with an app key
    connected: function () { return !!(auth && auth.refresh); },
    account: function () { return auth ? { name: auth.name || '', email: auth.email || '' } : null; },
    folder: FOLDER,
    connect: connect,
    codeUrl: codeUrl,
    finishCode: finishCode,
    isReturn: isReturn,
    finishRedirect: finishRedirect,
    disconnect: disconnect,
    download: download,
    upload: upload,
    remove: remove
  };
})();
