// Enhanced navigation swaps the page without a reload, so the popover would stay open over the
// new page. Close it whenever one of its links is followed. Delegated so it survives DOM patching.
document.addEventListener("click", function (event) {
    if (event.target.closest("#site-menu a")) {
        document.getElementById("site-menu")?.hidePopover();
    }
});
