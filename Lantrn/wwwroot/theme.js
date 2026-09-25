// Drives Bootstrap 5.3's data-bs-theme switch from the OS color-scheme
// preference. Loaded synchronously from <head> so the attribute is set
// before first paint, otherwise a dark-mode visitor sees a white flash.
(function () {
    var media = window.matchMedia('(prefers-color-scheme: dark)');

    function apply() {
        var theme = media.matches ? 'dark' : 'light';
        if (document.documentElement.getAttribute('data-bs-theme') !== theme) {
            document.documentElement.setAttribute('data-bs-theme', theme);
        }
    }

    apply();
    media.addEventListener('change', apply);

    // Blazor's enhanced navigation syncs <html> attributes with the server-rendered
    // page, which has no data-bs-theme, so put it back whenever it is stripped.
    new MutationObserver(apply).observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['data-bs-theme'],
    });
})();
