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

    // --- PDF ilk sayfa küçük resmi (pdf.js, yerel kopya: /lib/pdfjs) -------------------------
    // [data-pdf-thumb] kutucukları görünür olunca ilk sayfayı içlerindeki [data-pdf-canvas]'a çizer
    // ve [data-pdf-fallback] ikonunu gizler. Her kutucuk en çok bir kez denenir; herhangi bir hata
    // olursa yedek ikon + dosya adı kalır ve kutucuk tıklanabilir olmaya devam eder.
    const maxConcurrentThumbs = 2;
    const thumbQueue = [];
    const thumbCache = new Map();
    let activeThumbs = 0;
    let pdfjsPromise = null;

    function loadPdfjs() {
        pdfjsPromise ??= import('/lib/pdfjs/pdf.min.mjs').then(lib => {
            lib.GlobalWorkerOptions.workerSrc = '/lib/pdfjs/pdf.worker.min.mjs';
            return lib;
        });
        return pdfjsPromise;
    }

    // url -> ilk sayfanın çizildiği canvas (aynı dosya iki kez indirilip çizilmesin).
    function renderFirstPage(url, maxWidth, maxHeight) {
        const key = url + '|' + Math.round(maxWidth) + 'x' + Math.round(maxHeight);
        if (!thumbCache.has(key)) {
            const job = (async () => {
                const lib = await loadPdfjs();
                const response = await fetch(url, { credentials: 'same-origin' });
                if (!response.ok) throw new Error('Dosya okunamadı.');
                const data = new Uint8Array(await response.arrayBuffer());
                const pdf = await lib.getDocument({ data }).promise;
                try {
                    const page = await pdf.getPage(1);
                    const base = page.getViewport({ scale: 1 });
                    const scale = Math.min(maxWidth / base.width, maxHeight / base.height);
                    const ratio = window.devicePixelRatio || 1;
                    const viewport = page.getViewport({ scale: scale * ratio });
                    const canvas = document.createElement('canvas');
                    canvas.width = Math.max(1, Math.floor(viewport.width));
                    canvas.height = Math.max(1, Math.floor(viewport.height));
                    await page.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
                    canvas.dataset.cssWidth = String(Math.floor(viewport.width / ratio));
                    canvas.dataset.cssHeight = String(Math.floor(viewport.height / ratio));
                    return canvas;
                } finally {
                    pdf.destroy();
                }
            })();
            job.catch(() => thumbCache.delete(key));
            thumbCache.set(key, job);
        }
        return thumbCache.get(key);
    }

    function paintCanvas(target, source) {
        target.width = source.width;
        target.height = source.height;
        target.style.width = source.dataset.cssWidth + 'px';
        target.style.height = source.dataset.cssHeight + 'px';
        target.getContext('2d').drawImage(source, 0, 0);
    }

    async function renderTile(tile) {
        const canvas = tile.querySelector('[data-pdf-canvas]');
        if (!canvas) return;
        try {
            // Kutucuğun içeriği (dosya adı satırı + boşluklar) için yer ayırır.
            const source = await renderFirstPage(
                tile.dataset.attachmentUrl,
                Math.max(40, tile.clientWidth - 24),
                Math.max(40, tile.clientHeight - 44));
            paintCanvas(canvas, source);
            canvas.style.display = 'block';
            const fallback = tile.querySelector('[data-pdf-fallback]');
            if (fallback) fallback.style.display = 'none';
        } catch {
            // Yedek ikon + dosya adı kalır.
        }
    }

    function pumpThumbs() {
        while (activeThumbs < maxConcurrentThumbs && thumbQueue.length > 0) {
            const tile = thumbQueue.shift();
            activeThumbs++;
            renderTile(tile).finally(() => {
                activeThumbs--;
                pumpThumbs();
            });
        }
    }

    function queueThumb(tile) {
        // Kapalı modal içindeki kutucuğun ölçüsü yoktur; açılınca tekrar denenir.
        if (tile.dataset.pdfThumbState || tile.clientWidth === 0) return;
        tile.dataset.pdfThumbState = 'queued';
        thumbQueue.push(tile);
        pumpThumbs();
    }

    function initPdfThumbnails() {
        const tiles = document.querySelectorAll('[data-pdf-thumb]');
        if (tiles.length === 0) return;

        if ('IntersectionObserver' in window) {
            const observer = new IntersectionObserver(entries => {
                for (const entry of entries) {
                    if (!entry.isIntersecting) continue;
                    observer.unobserve(entry.target);
                    queueThumb(entry.target);
                }
            }, { rootMargin: '200px' });
            tiles.forEach(tile => observer.observe(tile));
        } else {
            tiles.forEach(queueThumb);
        }

        // Masaüstünde kutucuklar kapalı <dialog> içindedir; modal açılınca görünenleri çiz.
        new MutationObserver(records => {
            for (const record of records) {
                const dialog = record.target;
                if (dialog instanceof HTMLDialogElement && dialog.open)
                    dialog.querySelectorAll('[data-pdf-thumb]').forEach(queueThumb);
            }
        }).observe(document.body, { attributes: true, attributeFilter: ['open'], subtree: true });
    }

    // Listedeki ataş önizlemesi için: ilk ek PDF ise aynı küçük resim hedef canvas'a çizilir.
    window.renderPdfPreview = async (url, canvas, size) => {
        try {
            paintCanvas(canvas, await renderFirstPage(url, size, size));
            return true;
        } catch {
            return false;
        }
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initPdfThumbnails);
    else initPdfThumbnails();

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
