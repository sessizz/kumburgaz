// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// ── jQuery Validate: Türkçe ondalık (virgüllü) destek ──────────────────────
// Tarayıcı doğrulaması ondalık ayırıcı olarak nokta bekler,
// Türkçe klavyede virgül kullanıldığından hem virgül hem nokta kabul et.
$(function () {
    if (typeof $.validator === 'undefined') return;

    // number kuralını Türkçe virgüle izin verecek şekilde genişlet
    $.validator.methods.number = function (value, element) {
        return this.optional(element) ||
            /^-?(\d+[.,]?\d*|[.,]\d+)([eE][+-]?\d+)?$/.test(value.trim());
    };

    // Türkçe hata mesajları
    $.extend($.validator.messages, {
        required:  "Bu alan zorunludur.",
        number:    "Lütfen geçerli bir sayı giriniz.",
        digits:    "Lütfen sadece rakam giriniz.",
        min:       $.validator.format("Lütfen {0} veya daha büyük bir değer giriniz."),
        max:       $.validator.format("Lütfen {0} veya daha küçük bir değer giriniz."),
        range:     $.validator.format("Lütfen {0} ile {1} arasında bir değer giriniz."),
        maxlength: $.validator.format("Lütfen en fazla {0} karakter giriniz."),
        minlength: $.validator.format("Lütfen en az {0} karakter giriniz."),
        rangelength: $.validator.format("Lütfen {0} ile {1} karakter arasında bir değer giriniz."),
        email:     "Lütfen geçerli bir e-posta adresi giriniz.",
        url:       "Lütfen geçerli bir URL giriniz.",
        equalTo:   "Lütfen aynı değeri tekrar giriniz."
    });

    // Unobtrusive Validation adaptörlerini güncelle
    if (typeof $.validator.unobtrusive !== 'undefined') {
        $.validator.unobtrusive.parseElement = (function (original) {
            return function () { return original.apply(this, arguments); };
        })($.validator.unobtrusive.parseElement);
    }
});
