const sheetQuery = window.matchMedia("(max-width: 991.98px)");

let onKeyDown;

const isTyping = (element) =>
    element instanceof HTMLElement &&
    (element.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(element.tagName));

export function init(dotnet, input) {
    onKeyDown = async (e) => {
        if (e.defaultPrevented || e.altKey) {
            return;
        }

        if (e.ctrlKey || e.metaKey || e.shiftKey) {
            return;
        }

        // From the search box, the down arrow hands the keyboard over to the result list.
        if (e.target === input) {
            if (e.key === "ArrowDown" && document.querySelector(".result-row:not(.is-skeleton)")) {
                e.preventDefault();
                input.blur();
                await dotnet.invokeMethodAsync("MoveSelection", 0);
            } else if (e.key === "Escape") {
                input.blur();
            }
            return;
        }

        if (isTyping(e.target)) {
            return;
        }

        switch (e.key) {
            case "ArrowDown":
            case "j":
                e.preventDefault();
                await dotnet.invokeMethodAsync("MoveSelection", 1);
                break;

            case "ArrowUp":
            case "k":
                e.preventDefault();
                // Moving up from the first result goes back to the search box.
                if (!await dotnet.invokeMethodAsync("MoveSelection", -1)) {
                    input.focus();
                }
                break;

            case "Enter": {
                // A focused link or button handles Enter itself.
                if (e.target.closest?.("a, button")) {
                    return;
                }
                const link = document.querySelector(".result-row.is-selected .result-title[href]");
                if (link) {
                    e.preventDefault();
                    link.click();
                }
                break;
            }

            case "Escape":
                if (sheetQuery.matches && document.querySelector(".preview.is-open")) {
                    await dotnet.invokeMethodAsync("ClosePreview");
                } else {
                    input.focus();
                }
                break;
        }
    };

    document.addEventListener("keydown", onKeyDown);
}

// Called after each render that changed the selection; the preview scrolls to its own match.
export function revealSelected() {
    document.querySelector(".result-row.is-selected")?.scrollIntoView({ block: "nearest" });
}

export function dispose() {
    document.removeEventListener("keydown", onKeyDown);
}
