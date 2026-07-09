/* Kumburgaz mobil PWA - Anlik bildirim (Web Push) abonelik yonetimi.
   Sadece /m/Bildirimler sayfasinda yuklenir; push destegi yoksa kart gizli kalir. */
(function () {
    'use strict';

    function urlBase64ToUint8Array(base64String) {
        var padding = '='.repeat((4 - (base64String.length % 4)) % 4);
        var base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
        var rawData = atob(base64);
        var outputArray = new Uint8Array(rawData.length);
        for (var i = 0; i < rawData.length; ++i) {
            outputArray[i] = rawData.charCodeAt(i);
        }
        return outputArray;
    }

    function postForm(url, fields) {
        var body = new URLSearchParams(fields);
        return fetch(url, { method: 'POST', credentials: 'same-origin', body: body });
    }

    document.addEventListener('DOMContentLoaded', function () {
        var card = document.getElementById('pushCard');
        var scriptTag = document.currentScript || document.querySelector('script[data-public-key]');
        if (!card || !scriptTag) {
            return;
        }

        if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
            return;
        }

        var publicKey = scriptTag.getAttribute('data-public-key');
        if (!publicKey) {
            return;
        }

        var btn = document.getElementById('pushToggleBtn');
        var status = document.getElementById('pushStatus');
        var tokenInput = card.querySelector('input[name="__RequestVerificationToken"]');
        var token = tokenInput ? tokenInput.value : '';
        var subscribeUrl = '/m/Bildirimler/Abone';
        var unsubscribeUrl = '/m/Bildirimler/AbonelikSil';

        card.style.display = 'block';

        function setUiSubscribed(subscribed) {
            btn.textContent = subscribed ? 'Anlık bildirimleri kapat' : 'Anlık bildirimleri aç';
            status.textContent = subscribed ? 'Anlık bildirimler açık.' : '';
        }

        navigator.serviceWorker.ready
            .then(function (reg) { return reg.pushManager.getSubscription(); })
            .then(function (sub) { setUiSubscribed(!!sub); })
            .catch(function () { /* durum bilinmiyor, buton varsayilan "ac" kalir */ });

        btn.addEventListener('click', function () {
            btn.disabled = true;
            navigator.serviceWorker.ready.then(function (reg) {
                reg.pushManager.getSubscription().then(function (existing) {
                    if (existing) {
                        var endpoint = existing.endpoint;
                        existing.unsubscribe().then(function () {
                            return postForm(unsubscribeUrl, { endpoint: endpoint, __RequestVerificationToken: token });
                        }).then(function () {
                            setUiSubscribed(false);
                            btn.disabled = false;
                        }).catch(function () {
                            status.textContent = 'İşlem başarısız oldu.';
                            btn.disabled = false;
                        });
                        return;
                    }

                    Notification.requestPermission().then(function (permission) {
                        if (permission !== 'granted') {
                            status.textContent = 'Bildirim izni verilmedi.';
                            btn.disabled = false;
                            return;
                        }

                        reg.pushManager.subscribe({
                            userVisibleOnly: true,
                            applicationServerKey: urlBase64ToUint8Array(publicKey)
                        }).then(function (sub) {
                            var json = sub.toJSON();
                            return postForm(subscribeUrl, {
                                endpoint: json.endpoint,
                                p256dh: json.keys.p256dh,
                                auth: json.keys.auth,
                                __RequestVerificationToken: token
                            });
                        }).then(function () {
                            setUiSubscribed(true);
                            btn.disabled = false;
                        }).catch(function () {
                            status.textContent = 'Abonelik oluşturulamadı.';
                            btn.disabled = false;
                        });
                    });
                });
            });
        });
    });
})();
