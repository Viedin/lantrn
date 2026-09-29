namespace Lantrn.Services.Search;

// Neighbouring chunks are read together in the preview and by the assistant, so listing them apart wastes result slots.
public static class HitCollapser
{
    private const int MaxHitsPerDocument = 3;

    // Expects hits best first, and keeps that order.
    public static IReadOnlyList<SearchHit> Collapse(IReadOnlyList<SearchHit> hits, int limit)
    {
        var kept = new List<SearchHit>(limit);
        foreach (var hit in hits)
        {
            if (kept.Count == limit)
            {
                break;
            }

            var sameDocument = kept.Where(k => k.DocumentId == hit.DocumentId).ToList();
            var overlaps = sameDocument.Any(k => Math.Abs(k.ChunkIndex - hit.ChunkIndex) <= QdrantStore.PassageRadius);
            if (!overlaps && sameDocument.Count < MaxHitsPerDocument)
            {
                kept.Add(hit);
            }
        }

        return kept;
    }
}
