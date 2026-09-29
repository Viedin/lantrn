using Lantrn.Infra;
using Lantrn.Services.Search;

namespace Lantrn.Test.Search;

public class HitCollapserTests
{
    private static readonly Guid DocumentA = Guid.NewGuid();
    private static readonly Guid DocumentB = Guid.NewGuid();

    [Fact]
    public void Neighbouring_chunks_give_way_to_the_best_of_them()
    {
        var hits = Collapse(10, (DocumentA, 5), (DocumentA, 6), (DocumentA, 4), (DocumentA, 7));

        Assert.Equal([(DocumentA, 5L), (DocumentA, 7L)], hits);
    }

    [Fact]
    public void The_same_chunk_index_in_another_document_is_kept()
    {
        var hits = Collapse(10, (DocumentA, 3), (DocumentB, 3));

        Assert.Equal([(DocumentA, 3L), (DocumentB, 3L)], hits);
    }

    [Fact]
    public void One_document_takes_at_most_three_results()
    {
        var hits = Collapse(10, (DocumentA, 0), (DocumentA, 10), (DocumentA, 20), (DocumentA, 30), (DocumentB, 0));

        Assert.Equal([(DocumentA, 0L), (DocumentA, 10L), (DocumentA, 20L), (DocumentB, 0L)], hits);
    }

    [Fact]
    public void Stops_at_the_limit_in_the_order_given()
    {
        var hits = Collapse(2, (DocumentB, 0), (DocumentA, 0), (DocumentA, 10));

        Assert.Equal([(DocumentB, 0L), (DocumentA, 0L)], hits);
    }

    private static List<(Guid, long)> Collapse(int limit, params (Guid Document, long Chunk)[] hits) =>
        HitCollapser.Collapse(hits.Select(h => Hit(h.Document, h.Chunk)).ToList(), limit)
            .Select(h => (h.DocumentId, h.ChunkIndex))
            .ToList();

    private static SearchHit Hit(Guid documentId, long chunkIndex) =>
        new(1f, "text", "", "file.md", [], DocumentKind.Text, documentId, chunkIndex, null, null);
}
