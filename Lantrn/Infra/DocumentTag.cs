namespace Lantrn.Infra;

// Mirrors the "tags" payload on the document's Qdrant points, so tags can be listed and counted without Qdrant.
public sealed class DocumentTag
{
    public Guid DocumentId { get; set; }

    public required string Name { get; set; }
}
