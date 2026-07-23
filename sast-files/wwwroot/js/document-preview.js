(() => {
    let objectUrl = null;

    function clearPreview(container) {
        if (objectUrl) {
            URL.revokeObjectURL(objectUrl);
            objectUrl = null;
        }
        container.replaceChildren();
    }

    function showMessage(container, message) {
        const element = document.createElement('div');
        element.className = 'document-preview-message';
        element.textContent = message;
        container.append(element);
    }

    function previewImage(container, bytes, contentType) {
        objectUrl = URL.createObjectURL(new Blob([bytes], { type: contentType }));
        const image = document.createElement('img');
        image.className = 'document-preview-image';
        image.src = objectUrl;
        image.alt = 'Belge görseli';
        container.append(image);
    }

    function previewPdf(container, bytes) {
        objectUrl = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
        const frame = document.createElement('iframe');
        frame.className = 'document-preview-pdf';
        frame.src = objectUrl;
        frame.title = 'PDF önizleme';
        container.append(frame);
    }

    function previewWorkbook(container, bytes) {
        const workbook = XLSX.read(bytes);
        const tabs = document.createElement('div');
        tabs.className = 'document-preview-sheet-tabs';
        const tableHost = document.createElement('div');

        const renderSheet = (name, button) => {
            tabs.querySelectorAll('button').forEach(item => item.classList.remove('active'));
            button.classList.add('active');
            tableHost.replaceChildren();
            const table = document.createElement('table');
            table.className = 'document-preview-table';
            const rows = XLSX.utils.sheet_to_json(workbook.Sheets[name], { header: 1, raw: false, defval: '' });
            rows.forEach(row => {
                const tr = document.createElement('tr');
                row.forEach(value => {
                    const td = document.createElement('td');
                    td.textContent = String(value);
                    tr.append(td);
                });
                table.append(tr);
            });
            tableHost.append(table);
        };

        workbook.SheetNames.forEach((name, index) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = name;
            button.addEventListener('click', () => renderSheet(name, button));
            tabs.append(button);
            if (index === 0) renderSheet(name, button);
        });

        container.append(tabs, tableHost);
    }

    async function render(button) {
        const container = document.getElementById('documentPreview');
        if (!container) return;

        document.querySelectorAll('[data-document-preview]').forEach(item => item.classList.remove('btn-primary'));
        button.classList.add('btn-primary');
        clearPreview(container);
        showMessage(container, 'Dosya yükleniyor...');

        try {
            const response = await fetch(button.dataset.previewUrl, { credentials: 'same-origin' });
            if (!response.ok) throw new Error('Dosya okunamadı.');
            const bytes = await response.arrayBuffer();
            const contentType = (button.dataset.contentType || '').toLowerCase();
            clearPreview(container);

            if (contentType === 'application/pdf') previewPdf(container, bytes);
            else if (contentType.startsWith('image/')) previewImage(container, bytes, contentType);
            else if (contentType === 'application/vnd.openxmlformats-officedocument.wordprocessingml.document') {
                if (!window.docx) throw new Error('DOCX önizleme bileşeni yüklenemedi.');
                await window.docx.renderAsync(bytes, container, container, { inWrapper: true, breakPages: true });
            } else if (contentType === 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' ||
                       contentType === 'application/vnd.ms-excel' ||
                       contentType === 'text/csv' || contentType === 'application/csv') {
                if (!window.XLSX) throw new Error('Excel önizleme bileşeni yüklenemedi.');
                previewWorkbook(container, bytes);
            } else if (contentType === 'text/plain') {
                const text = document.createElement('pre');
                text.className = 'document-preview-text';
                text.textContent = new TextDecoder().decode(bytes);
                container.append(text);
            } else {
                showMessage(container, 'Bu dosya türü tarayıcıda önizlenemiyor. Dosyayı indirebilirsiniz.');
            }
        } catch (error) {
            clearPreview(container);
            showMessage(container, error instanceof Error ? error.message : 'Dosya önizlenemedi.');
        }
    }

    document.querySelectorAll('[data-document-preview]').forEach(button => {
        button.addEventListener('click', () => render(button));
    });
})();
