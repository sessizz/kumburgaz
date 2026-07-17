/* "Telefondan ekle" paneli: masaustunde bir yakalama oturumu baslatir (QR + push),
   telefonda yuklenen dosyalari ~3sn'de bir sorgulayip kucuk gorseller olarak gosterir.
   Form kaydedilince gizli "captureToken" alani gonderilir; sunucu (Ledger/Documents
   Controller) o oturumdaki dosyalari ilgili kayda ekler. */
(function () {
    'use strict';

    function getAntiforgeryToken(form) {
        var scope = form || document;
        var input = scope.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function initPanel(panelRoot) {
        var purpose = panelRoot.dataset.purpose || 'gider';
        var uid = panelRoot.dataset.uid;
        var startBtn = document.getElementById('captureStartBtn-' + uid);
        var panel = document.getElementById('capturePanel-' + uid);
        var qrImg = document.getElementById('captureQr-' + uid);
        var filesWrap = document.getElementById('captureFiles-' + uid);
        var tokenInput = document.getElementById('captureToken-' + uid);
        var form = panelRoot.closest('form');
        if (!startBtn || !panel || !qrImg || !filesWrap || !tokenInput) {
            return;
        }

        var pollTimer = null;
        var knownIds = Object.create(null);

        function stopPolling() {
            if (pollTimer) {
                clearInterval(pollTimer);
                pollTimer = null;
            }
        }

        function removeFile(fileId, chip) {
            var body = new URLSearchParams({
                token: tokenInput.value,
                id: fileId,
                __RequestVerificationToken: getAntiforgeryToken(form)
            });
            fetch('/Capture/DosyaSil', { method: 'POST', credentials: 'same-origin', body: body })
                .then(function () {
                    chip.remove();
                    delete knownIds[fileId];
                });
        }

        function renderFile(file) {
            if (knownIds[file.id]) {
                return;
            }
            knownIds[file.id] = true;

            var chip = document.createElement('div');
            chip.style.cssText = 'position:relative;width:64px;height:64px;';

            var img = document.createElement('img');
            img.src = file.url;
            img.alt = file.fileName || '';
            img.style.cssText = 'width:64px;height:64px;object-fit:cover;border-radius:8px;border:1px solid #e5e7eb;background:#fff;';
            img.onerror = function () {
                var fallback = document.createElement('div');
                fallback.textContent = '📄';
                fallback.style.cssText = 'width:64px;height:64px;display:flex;align-items:center;justify-content:center;border-radius:8px;border:1px solid #e5e7eb;background:#fff;font-size:22px;';
                img.replaceWith(fallback);
            };

            var removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.title = 'Kaldır';
            removeBtn.textContent = '✕';
            removeBtn.style.cssText = 'position:absolute;top:-6px;right:-6px;width:20px;height:20px;line-height:20px;border-radius:999px;background:#dc2626;color:#fff;border:none;font-size:11px;cursor:pointer;padding:0;';
            removeBtn.addEventListener('click', function () {
                removeFile(file.id, chip);
            });

            chip.appendChild(img);
            chip.appendChild(removeBtn);
            filesWrap.appendChild(chip);
        }

        function poll() {
            if (!tokenInput.value) {
                return;
            }

            fetch('/Capture/Durum?token=' + encodeURIComponent(tokenInput.value), { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    if (!data) {
                        return;
                    }
                    (data.files || []).forEach(renderFile);
                })
                .catch(function () { /* bir sonraki turda tekrar dene */ });
        }

        startBtn.addEventListener('click', function () {
            startBtn.disabled = true;
            var body = new URLSearchParams({
                purpose: purpose,
                __RequestVerificationToken: getAntiforgeryToken(form)
            });
            fetch('/Capture/Baslat', { method: 'POST', credentials: 'same-origin', body: body })
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    tokenInput.value = data.token;
                    qrImg.src = data.qrDataUri;
                    panel.classList.remove('hidden');
                    stopPolling();
                    pollTimer = setInterval(poll, 3000);
                })
                .catch(function () { /* kullanici tekrar deneyebilir */ })
                .finally(function () {
                    startBtn.disabled = false;
                });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.phone-capture').forEach(initPanel);
    });
})();
