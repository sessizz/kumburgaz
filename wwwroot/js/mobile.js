/* Kumburgaz mobil PWA istemci scripti - Asama 1
   Su an yalnizca service worker kaydini yapar. Zil/push ilerleyen asamalarda eklenecek. */
(function () {
    'use strict';

    if ('serviceWorker' in navigator) {
        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(function (err) {
                console.warn('Service worker kaydi basarisiz:', err);
            });
        });
    }
})();
