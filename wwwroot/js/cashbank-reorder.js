// Kasa/banka detayında bir günün hareketlerini sürükle-bırak ile yeniden sıralar.
// Ekran yeniden eskiye sıralıdır; sunucuya eskiden yeniye gönderilir.
(() => {
    const root = document.querySelector('[data-bulk-transactions]');
    if (!root || typeof Sortable === 'undefined') return;

    const lists = Array.from(root.querySelectorAll('ul[data-reorder-day]'))
        .filter(list => list.querySelector('[data-reorder-handle]'));
    if (lists.length === 0) return;

    const kind = root.dataset.reorderKind;
    const accountId = root.dataset.reorderAccount;
    const bulkActions = root.querySelector('[data-bulk-actions]');
    const errorBox = document.querySelector('[data-action-error]');
    const errorText = document.querySelector('[data-action-error-text]');
    const sortables = [];
    let busy = false;

    const showError = (message) => {
        if (!errorBox || !errorText) return;
        errorText.textContent = message;
        errorBox.classList.remove('hidden');
        errorBox.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    };

    const clearError = () => errorBox?.classList.add('hidden');

    const isBulkMode = () => !!bulkActions && !bulkActions.classList.contains('hidden');

    // Toplu seçim modunda ya da kayıt sürerken sürükleme kapalıdır.
    const refreshEnabled = () => {
        const enabled = !busy && !isBulkMode();
        sortables.forEach(sortable => sortable.option('disabled', !enabled));
        root.querySelectorAll('[data-reorder-handle]').forEach(handle =>
            handle.classList.toggle('hidden', isBulkMode()));
    };

    const restore = (list, previous) => previous.forEach(item => list.appendChild(item));

    const save = async (list, previous) => {
        const rows = Array.from(list.querySelectorAll(':scope > li[data-tx]'))
            .map(item => item.dataset.tx)
            .reverse();
        const body = new URLSearchParams();
        body.append('kind', kind);
        body.append('id', accountId);
        body.append('day', list.dataset.reorderDay);
        rows.forEach(row => body.append('rows', row));
        body.append('__RequestVerificationToken',
            document.querySelector('input[name=__RequestVerificationToken]')?.value ?? '');

        busy = true;
        refreshEnabled();
        clearError();
        try {
            const response = await fetch('/CashBank/ReorderDay', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'Accept': 'application/json' },
                credentials: 'same-origin',
                body
            });
            if (response.ok) {
                location.reload();
                return;
            }
            let message = 'Sıralama kaydedilemedi.';
            try { message = (await response.json()).error || message; } catch { /* JSON olmayan yanıt */ }
            restore(list, previous);
            showError(message);
        } catch {
            restore(list, previous);
            showError('Sıralama kaydedilemedi; bağlantıyı kontrol edip tekrar deneyin.');
        }
        busy = false;
        refreshEnabled();
    };

    lists.forEach(list => {
        let previous = [];
        sortables.push(Sortable.create(list, {
            draggable: 'li[data-tx]',
            handle: '[data-reorder-handle]',
            animation: 150,
            onStart: () => { previous = Array.from(list.children); },
            onEnd: (event) => {
                if (event.oldIndex === event.newIndex) return;
                save(list, previous);
            }
        }));
    });

    if (bulkActions) {
        new MutationObserver(refreshEnabled).observe(bulkActions, { attributes: true, attributeFilter: ['class'] });
    }
    refreshEnabled();
})();
