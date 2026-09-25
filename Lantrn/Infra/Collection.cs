namespace Lantrn.Infra;

public sealed class Collection
{
    // Also the Qdrant collection name. Qdrant cannot rename a collection, so the name doubles as the key.
    public required string Name { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Document> Documents { get; set; } = [];
}
