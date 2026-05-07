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
