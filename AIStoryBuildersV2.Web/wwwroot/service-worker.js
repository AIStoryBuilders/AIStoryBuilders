// V2 is not an offline app and index.html never registers this file. It exists only for browsers that visited the V1
// Online app on the same origin: V1 installed a service worker that answers every page load from its own cache, and it
// keeps doing so until its script is replaced. Those browsers still check this URL for updates, so this replacement
// removes V1's cached files, unregisters itself and reloads the page, which then comes from the server.
// It touches only the service worker and V1's offline cache. localStorage, including V1's stories, is left alone.
self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', event => event.waitUntil((async () => {
    await self.clients.claim();

    const cacheNames = await caches.keys();
    await Promise.all(cacheNames
        .filter(name => name.startsWith('offline-cache-'))
        .map(name => caches.delete(name)));

    await self.registration.unregister();

    const pages = await self.clients.matchAll({ type: 'window' });
    await Promise.all(pages.map(page => page.navigate(page.url)));
})()));
