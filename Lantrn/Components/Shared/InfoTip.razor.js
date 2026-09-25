export function init(element) {
    bootstrap.Tooltip.getOrCreateInstance(element);
}

// A tooltip left open when its element is removed would stay on screen.
export function dispose(element) {
    bootstrap.Tooltip.getInstance(element)?.dispose();
}
