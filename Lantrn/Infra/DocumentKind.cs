namespace Lantrn.Infra;

public enum DocumentKind
{
    Text,

    // Text read from an image by OCR; the image itself is kept as the document's original.
    Ocr,
}
