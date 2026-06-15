// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// ── jQuery Validate: Türkçe ondalık (virgüllü) destek ──────────────────────
// Tarayıcı doğrulaması ondalık ayırıcı olarak nokta bekler,
// Türkçe klavyede virgül kullanıldığından hem virgül hem nokta kabul et.

// Virgülü noktaya normalize eden yardımcı fonksiyon
function normalizeDecimal(value) {
    return typeof value === 'string' ? value.trim().replace(',', '.') : value;
}

$(function () {
    if (typeof $.validator === 'undefined') return;

    // number: virgülü kabul et
    $.validator.methods.number = function (value, element) {
        return this.optional(element) ||
            /^-?(\d+[.,]?\d*|[.,]\d+)([eE][+-]?\d+)?$/.test(value.trim());
    };

    // range: virgüllü sayıyı noktaya çevirerek karşılaştır
    $.validator.methods.range = function (value, element, param) {
        var val = parseFloat(normalizeDecimal(value));
        return this.optional(element) || (val >= param[0] && val <= param[1]);
    };

    // min / max da aynı şekilde
    $.validator.methods.min = function (value, element, param) {
        return this.optional(element) || parseFloat(normalizeDecimal(value)) >= param;
    };
    $.validator.methods.max = function (value, element, param) {
        return this.optional(element) || parseFloat(normalizeDecimal(value)) <= param;
    };

    // Türkçe hata mesajları
    $.extend($.validator.messages, {
        required:    "Bu alan zorunludur.",
        number:      "Lütfen geçerli bir sayı giriniz.",
        digits:      "Lütfen sadece rakam giriniz.",
        min:         $.validator.format("Lütfen {0} veya daha büyük bir değer giriniz."),
        max:         $.validator.format("Lütfen {0} veya daha küçük bir değer giriniz."),
        range:       $.validator.format("Lütfen {0} ile {1} arasında bir değer giriniz."),
        maxlength:   $.validator.format("Lütfen en fazla {0} karakter giriniz."),
        minlength:   $.validator.format("Lütfen en az {0} karakter giriniz."),
        rangelength: $.validator.format("Lütfen {0} ile {1} karakter arasında bir değer giriniz."),
        email:       "Lütfen geçerli bir e-posta adresi giriniz.",
        url:         "Lütfen geçerli bir URL giriniz.",
        equalTo:     "Lütfen aynı değeri tekrar giriniz."
    });
});

// ── Global daire araması (Ctrl+K / ⌘K) ─────────────────────────────────────
(function () {
    var root = document.querySelector('[data-unit-search]');
    if (!root) return;

    var input = root.querySelector('#global-unit-search');
    var results = root.querySelector('#global-unit-search-results');
    var searchUrl = root.getAttribute('data-search-url');
    var debounceTimer;
    var activeIndex = -1;
    var items = [];
    var abortController;

    function hideResults() {
        results.classList.add('hidden');
        results.innerHTML = '';
        input.setAttribute('aria-expanded', 'false');
        activeIndex = -1;
        items = [];
    }

    function setActive(index) {
        activeIndex = index;
        Array.from(results.querySelectorAll('[data-result-index]')).forEach(function (item, idx) {
            item.classList.toggle('bg-blue-50', idx === activeIndex);
            item.setAttribute('aria-selected', idx === activeIndex ? 'true' : 'false');
        });
    }

    function openUnit(item) {
        if (item && item.url) {
            window.location.href = item.url;
        }
    }

    function renderResults(data, term) {
        items = Array.isArray(data) ? data : [];
        activeIndex = -1;

        if (!items.length) {
            results.innerHTML = '<div class="px-4 py-3 text-sm text-gray-500">“' + escapeHtml(term) + '” için daire bulunamadı.</div>';
            results.classList.remove('hidden');
            input.setAttribute('aria-expanded', 'true');
            return;
        }

        results.innerHTML = items.map(function (item, index) {
            var owner = item.ownerName ? '<span class="text-gray-400">•</span><span>Malik: ' + escapeHtml(item.ownerName) + '</span>' : '';
            var tenant = item.tenantName ? '<span class="text-gray-400">•</span><span>Kiracı: ' + escapeHtml(item.tenantName) + '</span>' : '';
            var inactive = item.active ? '' : '<span class="ml-2 pill pill-muted normal-case">Pasif</span>';
            return '<button type="button" class="w-full text-left px-4 py-3 hover:bg-blue-50 focus:bg-blue-50 focus:outline-none border-b border-gray-100 last:border-b-0" role="option" aria-selected="false" data-result-index="' + index + '">' +
                '<span class="flex items-center justify-between gap-3">' +
                    '<span class="min-w-0">' +
                        '<span class="block text-sm font-semibold text-gray-900 truncate">' + escapeHtml(item.label) + inactive + '</span>' +
                        '<span class="mt-0.5 flex items-center gap-1.5 text-xs text-gray-500 truncate">Daire ' + escapeHtml(item.unitNo) + owner + tenant + '</span>' +
                    '</span>' +
                    '<span class="material-symbols-outlined text-gray-300">chevron_right</span>' +
                '</span>' +
            '</button>';
        }).join('');

        results.classList.remove('hidden');
        input.setAttribute('aria-expanded', 'true');
    }

    function escapeHtml(value) {
        return String(value || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function search() {
        var term = input.value.trim();
        if (!term) {
            hideResults();
            return;
        }

        if (abortController) {
            abortController.abort();
        }

        abortController = new AbortController();
        fetch(searchUrl + '?term=' + encodeURIComponent(term), {
            headers: { 'Accept': 'application/json' },
            signal: abortController.signal
        })
            .then(function (response) {
                if (!response.ok) throw new Error('Arama başarısız oldu.');
                return response.json();
            })
            .then(function (data) { renderResults(data, term); })
            .catch(function (error) {
                if (error.name === 'AbortError') return;
                results.innerHTML = '<div class="px-4 py-3 text-sm text-red-600">Arama sırasında hata oluştu.</div>';
                results.classList.remove('hidden');
                input.setAttribute('aria-expanded', 'true');
            });
    }

    input.addEventListener('input', function () {
        window.clearTimeout(debounceTimer);
        debounceTimer = window.setTimeout(search, 180);
    });

    input.addEventListener('keydown', function (event) {
        if (results.classList.contains('hidden') || !items.length) return;

        if (event.key === 'ArrowDown') {
            event.preventDefault();
            setActive(activeIndex < items.length - 1 ? activeIndex + 1 : 0);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            setActive(activeIndex > 0 ? activeIndex - 1 : items.length - 1);
        } else if (event.key === 'Enter') {
            event.preventDefault();
            openUnit(items[activeIndex >= 0 ? activeIndex : 0]);
        } else if (event.key === 'Escape') {
            hideResults();
        }
    });

    results.addEventListener('mousedown', function (event) {
        var target = event.target.closest('[data-result-index]');
        if (!target) return;
        event.preventDefault();
        openUnit(items[Number(target.getAttribute('data-result-index'))]);
    });

    document.addEventListener('keydown', function (event) {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
            event.preventDefault();
            input.focus();
            input.select();
        }
    });

    document.addEventListener('click', function (event) {
        if (!root.contains(event.target)) {
            hideResults();
        }
    });
})();
