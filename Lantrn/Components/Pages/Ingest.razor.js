// Puts pasted files, such as a screenshot, into the file input as if they had been chosen.
export function listenForPaste(inputId) {
    const onPaste = event => {
        const input = document.getElementById(inputId);
        const pasted = [...(event.clipboardData?.files ?? [])];
        if (!input || pasted.length === 0) {
            return;
        }

        event.preventDefault();
        const transfer = new DataTransfer();
        pasted.forEach((file, index) => transfer.items.add(withUsefulName(file, index)));
        input.files = transfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    };

    document.addEventListener('paste', onPaste);
    return { dispose: () => document.removeEventListener('paste', onPaste) };
}

// Screenshots all arrive as "image.png", and the file name is the document's source, so each paste would replace the last.
function withUsefulName(file, index) {
    if (!/^image\.\w+$/.test(file.name)) {
        return file;
    }

    const stamp = new Date().toISOString().slice(0, 19).replace(/[-:]/g, '').replace('T', '-');
    const extension = file.name.split('.').pop();
    return new File([file], `pasted-${stamp}${index > 0 ? `-${index + 1}` : ''}.${extension}`, { type: file.type });
}
