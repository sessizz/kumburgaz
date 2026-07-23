/* Kumburgaz mobil PWA istemci scripti.
   Service worker kaydi + zil rozeti polling (60 sn). Push Asama 5'te eklenecek. */
(function () {
    'use strict';

    if ('serviceWorker' in navigator) {
        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(function (err) {
                console.warn('Service worker kaydi basarisiz:', err);
            });
        });
    }

    function refreshBellBadge() {
        var badge = document.getElementById('bellBadge');
        if (!badge) {
            return;
        }

        fetch('/m/Bildirimler/Ozet', { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (data) {
                if (!data) {
                    return;
                }
                var count = data.okunmamis || 0;
                if (count > 0) {
                    badge.textContent = count > 99 ? '99+' : String(count);
                    badge.style.display = 'flex';
                } else {
                    badge.style.display = 'none';
                }
            })
            .catch(function () { /* sessiz gec, zil calismaya devam eder */ });
    }

    if (document.getElementById('bellBadge')) {
        refreshBellBadge();
        setInterval(refreshBellBadge, 60000);
    }
})();
