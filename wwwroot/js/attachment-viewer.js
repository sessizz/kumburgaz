// Gider eklerini türüne göre açar: resim -> mevcut fotoğraf lightbox'ı, PDF -> aynı sayfada
// çerçevede önizleme, diğer belgeler (DOCX/XLSX) -> indirme.
// Ek uçları dosyayı indirme (Content-Disposition: attachment) olarak döndürdüğü için PDF
// doğrudan iframe'e verilmez; Belgeler sayfasındaki gibi fetch + blob URL kullanılır.
// Kullanım: <button data-attachment-url=".." data-attachment-type=".." data-attachment-name=".."
//                   onclick="openAttachmentFrom(this)">
(() => {
    let objectUrl = null;

    function viewer() {
        let dialog = document.getElementById('attachmentViewer');
        if (dialog) return dialog;

        dialog = document.createElement('dialog');
        dialog.id = 'attachmentViewer';
        dialog.style.cssText = 'width:min(960px,96vw);height:92vh;max-width:none;max-height:none;padding:0;border:none;border-radius:12px;overflow:hidden;background:#fff;';
        dialog.innerHTML =
            '<div style="display:flex;flex-direction:column;height:100%;">' +
            '<div style="display:flex;align-items:center;gap:8px;padding:10px 14px;border-bottom:1px solid #e5e7eb;">' +
            '<span data-viewer-title style="flex:1;font-weight:600;font-size:14px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;"></span>' +
            '<a data-viewer-download style="font-size:13px;font-weight:600;text-decoration:none;color:#0f766e;">İndir</a>' +
            '<button type="button" data-viewer-close aria-label="Kapat" style="border:none;background:#f1f5f9;border-radius:999px;width:30px;height:30px;cursor:pointer;font-size:14px;">✕</button>' +
            '</div>' +
            '<div data-viewer-body style="flex:1;min-height:0;display:flex;align-items:center;justify-content:center;background:#f8fafc;"></div>' +
            '</div>';
        dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
        dialog.querySelector('[data-viewer-close]').addEventListener('click', () => dialog.close());
        dialog.addEventListener('close', () => {
            dialog.querySelector('[data-viewer-body]').replaceChildren();
            if (objectUrl) {
                URL.revokeObjectURL(objectUrl);
                objectUrl = null;
            }
        });
        document.body.append(dialog);
        return dialog;
    }

    function message(body, text) {
        const element = document.createElement('div');
        element.style.cssText = 'color:#64748b;font-size:14px;padding:24px;text-align:center;';
        element.textContent = text;
        body.replaceChildren(element);
    }

    async function openPdf(url, fileName) {
        const dialog = viewer();
        const body = dialog.querySelector('[data-viewer-body]');
        dialog.querySelector('[data-viewer-title]').textContent = fileName || 'PDF';
        const download = dialog.querySelector('[data-viewer-download]');
        download.href = url;
        download.setAttribute('download', fileName || '');
        message(body, 'Dosya yükleniyor...');
        if (!dialog.open) dialog.showModal();

        try {
            const response = await fetch(url, { credentials: 'same-origin' });
            if (!response.ok) throw new Error('Dosya okunamadı.');
            const bytes = await response.arrayBuffer();
            if (!dialog.open) return;
            objectUrl = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
            const frame = document.createElement('iframe');
            frame.src = objectUrl;
            frame.title = 'PDF önizleme';
            frame.style.cssText = 'width:100%;height:100%;border:none;background:#fff;';
            body.replaceChildren(frame);
        } catch (error) {
            message(body, error instanceof Error ? error.message : 'Dosya önizlenemedi.');
        }
    }

    function download(url, fileName) {
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', fileName || '');
        document.body.append(link);
        link.click();
        link.remove();
    }

    window.openAttachment = (url, contentType, fileName) => {
        const type = (contentType || '').toLowerCase();
        if (type.startsWith('image/') && typeof window.openLightbox === 'function') window.openLightbox(url);
        else if (type === 'application/pdf') openPdf(url, fileName);
        else download(url, fileName);
    };

    window.openAttachmentFrom = element => window.openAttachment(
        element.dataset.attachmentUrl,
        element.dataset.attachmentType,
        element.dataset.attachmentName);
})();
