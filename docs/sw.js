// QuickCal service worker: makes the app work offline and installable.
// Bump VERSION when you publish a new version, so users get the new files.
const VERSION = 'quickcal-v2.1.3';
const FILES = [
  './',
  './index.html',
  './styles.css',
  './app.js',
  './pip.js',
  './xlsx.js',
  './dropbox.js',
  './notes.js',
  './manifest.webmanifest',
  './assets/gold_texture.jpg',
  './icons/icon-192.png',
  './icons/icon-512.png',
  './icons/icon-maskable-512.png',
  './icons/favicon.png',
  './help/chrome-autostart.png'
];

self.addEventListener('install', (event) => {
  // cache: 'reload' skips the browser's own HTTP cache, so a new VERSION really gets the new files
  event.waitUntil(caches.open(VERSION).then((cache) => cache.addAll(FILES.map((f) => new Request(f, { cache: 'reload' })))));
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

// Cache first: the app opens from the cached copy at once, also when the PC has just started and the
// network is not up yet. Each file is downloaded again in the background and updates the cache, so a
// new version shows on the next start. (Bumping VERSION downloads all files fresh when the new
// service worker installs.) The beta is network first, so changes to it show at once.
const SLOW_NETWORK_MS = 1500;
self.addEventListener('fetch', (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin) return;
  const network = fetch(request).then((response) => {
    if (response.ok) {
      const copy = response.clone();
      caches.open(VERSION).then((cache) => cache.put(request, copy));
    }
    return response;
  });
  event.waitUntil(network.catch(() => {}));
  const cached = () => caches.match(request, { ignoreSearch: true });
  if (!url.pathname.includes('/beta/')) {
    event.respondWith(cached().then((c) => c || network.catch(() => caches.match('./index.html').then((i) => i || Response.error()))));
    return;
  }
  // Beta: network first, the cached copy if the network is slower than 1.5 s or offline
  event.respondWith(new Promise((resolve) => {
    let done = false;
    const answer = (r) => { if (!done && r) { done = true; resolve(r); } };
    const timer = setTimeout(() => cached().then(answer), SLOW_NETWORK_MS);
    network
      .then((r) => { clearTimeout(timer); answer(r); })
      .catch(() => cached()
        .then((c) => c || caches.match('./index.html'))
        .then((c) => answer(c || Response.error())));
  }));
});
