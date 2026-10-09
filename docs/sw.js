// QuickCal service worker: makes the app work offline and installable.
// Bump VERSION when you publish a new version, so users get the new files.
const VERSION = 'quickcal-v2.0.21';
const FILES = [
  './',
  './index.html',
  './styles.css',
  './app.js',
  './pip.js',
  './manifest.webmanifest',
  './assets/gold_texture.jpg',
  './icons/icon-192.png',
  './icons/icon-512.png',
  './icons/icon-maskable-512.png',
  './icons/favicon.png'
];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(VERSION).then((cache) => cache.addAll(FILES)));
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

// Network first (so updates arrive quickly), cached copy when offline.
// A slow network must not keep the app from opening: after 1.5 s the cached copy is used,
// while the download goes on in the background and updates the cache for next time.
const SLOW_NETWORK_MS = 1500;
self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET' || new URL(request.url).origin !== self.location.origin) return;
  const network = fetch(request).then((response) => {
    if (response.ok) {
      const copy = response.clone();
      caches.open(VERSION).then((cache) => cache.put(request, copy));
    }
    return response;
  });
  const cached = () => caches.match(request, { ignoreSearch: true });
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
  event.waitUntil(network.catch(() => {}));
});
