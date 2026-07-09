/* Kumburgaz mobil PWA - Anlik bildirim (Web Push) abonelik yonetimi.
   Layout'ta global yuklenir (push devre disi ise script hic eklenmez).
   Iki gorevi var: (1) Bildirimler sayfasindaki ac/kapa karti, (2) ilk mobil
   girişte otomatik "bildirimleri ac?" sorusu. */
(function () {
    'use strict';

    var ASKED_KEY = 'kumburgaz_push_asked';

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

    function getToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function postForm(url, fields) {
        fields.__RequestVerificationToken = getToken();
        var body = new URLSearchParams(fields);
        return fetch(url, { method: 'POST', credentials: 'same-origin', body: body });
    }

    // Kullanici jesti (buton tiklamasi) icinden cagrilmali; tarayicilar
    // Notification.requestPermission()'i jestsiz cagrilarda engeller/yok sayar.
    function subscribe(publicKey) {
        return navigator.serviceWorker.ready.then(function (reg) {
            return Notification.requestPermission().then(function (permission) {
                if (permission !== 'granted') {
                    return { ok: false, reason: 'denied' };
                }

                return reg.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: urlBase64ToUint8Array(publicKey)
                }).then(function (sub) {
                    var json = sub.toJSON();
                    return postForm('/m/Bildirimler/Abone', {
                        endpoint: json.endpoint,
                        p256dh: json.keys.p256dh,
                        auth: json.keys.auth
                    }).then(function () { return { ok: true }; });
                });
            });
        });
    }

    function unsubscribe() {
        return navigator.serviceWorker.ready.then(function (reg) {
            return reg.pushManager.getSubscription().then(function (existing) {
                if (!existing) {
                    return { ok: true };
                }

                var endpoint = existing.endpoint;
                return existing.unsubscribe().then(function () {
                    return postForm('/m/Bildirimler/AbonelikSil', { endpoint: endpoint });
                }).then(function () { return { ok: true }; });
            });
        });
    }

    function setupToggleCard(publicKey) {
        var card = document.getElementById('pushCard');
        if (!card) {
            return;
        }

        var btn = document.getElementById('pushToggleBtn');
        var status = document.getElementById('pushStatus');
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
                        unsubscribe().then(function () {
                            setUiSubscribed(false);
                            btn.disabled = false;
                        }).catch(function () {
                            status.textContent = 'İşlem başarısız oldu.';
                            btn.disabled = false;
                        });
                        return;
                    }

                    subscribe(publicKey).then(function (result) {
                        if (result.ok) {
                            setUiSubscribed(true);
                        } else {
                            status.textContent = 'Bildirim izni verilmedi.';
                        }
                        btn.disabled = false;
                    }).catch(function () {
                        status.textContent = 'Abonelik oluşturulamadı.';
                        btn.disabled = false;
                    });
                });
            });
        });
    }

    function setupAutoPrompt(publicKey) {
        var dialog = document.getElementById('pushPromptDialog');
        if (!dialog || localStorage.getItem(ASKED_KEY)) {
            return;
        }

        var yesBtn = document.getElementById('pushPromptYes');
        var noBtn = document.getElementById('pushPromptNo');

        function markAsked() {
            localStorage.setItem(ASKED_KEY, '1');
        }

        yesBtn.addEventListener('click', function () {
            yesBtn.disabled = true;
            subscribe(publicKey).finally(function () {
                markAsked();
                dialog.close();
            });
        });

        noBtn.addEventListener('click', function () {
            markAsked();
            dialog.close();
        });

        if (Notification.permission !== 'default') {
            // Kullanici tarayici duzeyinde zaten karar vermis (izin verdi/reddetti); tekrar sormaya gerek yok.
            markAsked();
            return;
        }

        navigator.serviceWorker.ready
            .then(function (reg) { return reg.pushManager.getSubscription(); })
            .then(function (existing) {
                if (existing) {
                    markAsked();
                    return;
                }
                dialog.showModal();
            })
            .catch(function () { /* servis calisani hazir degilse sessizce vazgec */ });
    }

    document.addEventListener('DOMContentLoaded', function () {
        if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
            return;
        }

        var scriptTag = document.currentScript || document.querySelector('script[data-public-key]');
        var publicKey = scriptTag ? scriptTag.getAttribute('data-public-key') : null;
        if (!publicKey) {
            return;
        }

        setupToggleCard(publicKey);
        setupAutoPrompt(publicKey);
    });
})();
