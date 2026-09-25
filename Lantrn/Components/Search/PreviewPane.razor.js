const highlightName = "bo-search";

// Called after each render that changed the selection or loaded its passage.
export function sync(preview, terms) {
    revealMatch(preview);
    highlight(preview, terms);
}

// The chunk before the match is shown first, so without this the match can sit below the fold,
// and a newly selected result would open at wherever the last one was scrolled to.
function revealMatch(preview) {
    if (!preview) {
        return;
    }

    // An image result leads with the image, which matters more than its transcription.
    const match = preview.querySelector(".passage.is-match");
    if (!match || preview.querySelector(".preview-image")) {
        preview.scrollTop = 0;
        return;
    }

    const offset = match.getBoundingClientRect().top - preview.getBoundingClientRect().top;
    preview.scrollTo({ top: preview.scrollTop + offset - 12, behavior: "instant" });
}

// Marks the terms with the CSS Custom Highlight API rather than wrapping them in elements,
// so the DOM Blazor rendered is left exactly as it was.
function highlight(root, terms) {
    if (!("highlights" in CSS)) {
        return;
    }

    CSS.highlights.delete(highlightName);
    if (!root || terms.length === 0) {
        return;
    }

    const escaped = terms.map(t => t.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));
    const pattern = new RegExp(`(?<![\\p{L}\\p{N}])(?:${escaped.join("|")})(?![\\p{L}\\p{N}])`, "giu");

    const ranges = [];
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        for (const match of node.data.matchAll(pattern)) {
            const range = new Range();
            range.setStart(node, match.index);
            range.setEnd(node, match.index + match[0].length);
            ranges.push(range);
        }
    }

    CSS.highlights.set(highlightName, new Highlight(...ranges));
}

export function dispose() {
    if ("highlights" in CSS) {
        CSS.highlights.delete(highlightName);
    }
}
