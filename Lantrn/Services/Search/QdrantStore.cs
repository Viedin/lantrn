using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf.Collections;
using Lantrn.Infra;
using Lantrn.Services.Ingestion;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Document = Qdrant.Client.Grpc.Document;

namespace Lantrn.Services.Search;

/// <summary>
/// Writes chunks (content + embedding + heading metadata) into a Qdrant collection.
/// </summary>
public sealed class QdrantStore(QdrantClient client, SettingsStore store, ILogger<QdrantStore> logger)
{
    private const string TagsField = "tags";
    private const string DocumentIdField = "document_id";
    private const string KindField = "kind";
    private const string ChunkIndexField = "chunk_index";

    // The sparse vector beside the unnamed dense one, filled by Qdrant's own BM25 from the chunk text.
    private const string KeywordVector = "keywords";
    private const string KeywordModel = "qdrant/bm25";

    // Words in a full chunk plus its document and section lines, for BM25's length normalisation.
    private const double AverageChunkWords = 300;

    // Each side of the fusion ranks this many candidates, so a match strong on only one side can still surface.
    private const ulong MinCandidates = 50;

    // Chunks on either side of a hit that are read with it, by people in the preview and by the assistant.
    public const int PassageRadius = 1;

    // Checked once per collection rather than on every store. Collections are only deleted through this class.
    private readonly ConcurrentDictionary<string, ulong> dimensionsByCollection = new();

    // Replaces the document's points with these (already embedded) chunks. The new points go in before
    // the old ones are removed, so a failure part way leaves the previous version searchable.
    public async Task<int> ReplaceDocumentAsync(
        string collection,
        string sourceFile,
        Guid documentId,
        DocumentKind kind,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(chunks.Count);
        await EnsureCollectionAsync(collection, (ulong)chunks[0].Embedding.Length, cancellationToken);

        var language = store.Current.Keywords.Language;
        var ids = chunks.Select(_ => Guid.NewGuid()).ToList();
        var points = chunks.Select((chunk, i) =>
        {
            var point = new PointStruct
            {
                Id = new PointId { Uuid = ids[i].ToString() },
                Vectors = HybridVectors(chunk.Embedding, Keywords(chunk.IndexText(sourceFile), language)),
            };

            point.Payload.Add("source", sourceFile);
            point.Payload.Add(DocumentIdField, documentId.ToString());
            point.Payload.Add(KindField, KindValue(kind));
            point.Payload.Add("content", chunk.Content);
            point.Payload.Add(ChunkIndexField, chunk.ChunkIndex);
            point.Payload.Add(TagsField, tags.ToArray());
            if (!string.IsNullOrEmpty(chunk.HeadingPath))
            {
                point.Payload.Add("heading_path", chunk.HeadingPath);
            }
            if (chunk.FirstPage is { } firstPage)
            {
                point.Payload.Add("first_page", firstPage);
            }
            if (chunk.LastPage is { } lastPage)
            {
                point.Payload.Add("last_page", lastPage);
            }

            return point;
        }).ToList();

        var stopwatch = Stopwatch.StartNew();
        await client.UpsertAsync(collection, points, cancellationToken: cancellationToken);

        var stale = DocumentFilter(documentId);
        stale.MustNot.Add(Conditions.HasId(ids));
        await client.DeleteAsync(collection, stale, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Upserted {Points} points from {FileName} into '{Collection}' with tags [{Tags}] in {Elapsed:N0} ms",
            points.Count, sourceFile, collection, string.Join(", ", tags), stopwatch.ElapsedMilliseconds);

        return points.Count;
    }

    public async Task<SearchResults> SearchAsync(
        string collection,
        string queryText,
        float[] queryVector,
        IReadOnlyList<string> tags,
        DocumentKind? kind = null,
        ulong limit = 5,
        CancellationToken cancellationToken = default)
    {
        // A query of only punctuation or single letters has no keywords to match on.
        var hybrid = SearchTerms.Of(queryText).Count > 0;

        logger.LogInformation(
            "Searching '{Collection}' ({Mode}) with a {Dimensions}-dim vector for the top {Limit}, tags [{Tags}], kind {Kind}",
            collection, hybrid ? "hybrid" : "meaning only", queryVector.Length, limit,
            string.Join(", ", tags), kind?.ToString() ?? "any");

        var filter = new Filter();

        // A point matches when it carries any of the selected tags.
        if (tags.Count > 0)
        {
            filter.Must.Add(Conditions.Match(TagsField, tags));
        }

        if (kind is { } wanted)
        {
            filter.Must.Add(Conditions.MatchKeyword(KindField, KindValue(wanted)));
        }

        var activeFilter = filter.Must.Count > 0 ? filter : null;
        var stopwatch = Stopwatch.StartNew();

        // Reciprocal rank fusion merges the two rankings by position, since cosine and BM25 scores aren't comparable.
        var points = hybrid
            ? await client.QueryAsync(
                collection,
                prefetch:
                [
                    Prefetch(queryVector, null, activeFilter, Math.Max(limit * 4, MinCandidates)),
                    Prefetch(Keywords(queryText, store.Current.Keywords.Language), KeywordVector, activeFilter, Math.Max(limit * 4, MinCandidates)),
                ],
                query: Fusion.Rrf,
                limit: limit,
                payloadSelector: true,
                cancellationToken: cancellationToken)
            : await client.QueryAsync(
                collection,
                query: queryVector,
                filter: activeFilter,
                limit: limit,
                payloadSelector: true,
                cancellationToken: cancellationToken);

        logger.LogInformation(
            "'{Collection}' returned {Hits} hits in {Elapsed:N0} ms (top score {Score})",
            collection, points.Count, stopwatch.ElapsedMilliseconds,
            points.Count > 0 ? points[0].Score : 0f);

        var hits = points.Select(point => new SearchHit(
            point.Score,
            Payload(point.Payload, "content"),
            Payload(point.Payload, "heading_path"),
            Payload(point.Payload, "source"),
            Tags(point),
            Payload(point.Payload, KindField) == KindValue(DocumentKind.Ocr) ? DocumentKind.Ocr : DocumentKind.Text,
            Guid.Parse(Payload(point.Payload, DocumentIdField)),
            IntPayload(point.Payload, ChunkIndexField) ?? 0,
            IntPayload(point.Payload, "first_page"),
            IntPayload(point.Payload, "last_page"))).ToList();

        return new SearchResults(hits, hybrid ? ScoreKind.Fusion : ScoreKind.Similarity);
    }

    // The chunks within radius of one hit, in document order, so a passage can be read in context.
    public async Task<IReadOnlyList<PassageChunk>> GetPassageAsync(
        string collection,
        Guid documentId,
        long chunkIndex,
        int radius,
        CancellationToken cancellationToken = default)
    {
        var filter = DocumentFilter(documentId);
        filter.Must.Add(Conditions.Range(ChunkIndexField, new Qdrant.Client.Grpc.Range
        {
            Gte = chunkIndex - radius,
            Lte = chunkIndex + radius,
        }));

        var response = await client.ScrollAsync(
            collection,
            filter,
            limit: (uint)(radius * 2 + 1),
            payloadSelector: true,
            cancellationToken: cancellationToken);

        return response.Result
            .Select(point => new PassageChunk(
                IntPayload(point.Payload, ChunkIndexField) ?? chunkIndex,
                Payload(point.Payload, "content"),
                Payload(point.Payload, "heading_path")))
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToList();
    }

    public async Task DeleteCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        dimensionsByCollection.TryRemove(collection, out _);
        if (!await client.CollectionExistsAsync(collection, cancellationToken))
        {
            return;
        }

        await client.DeleteCollectionAsync(collection, cancellationToken: cancellationToken);
        logger.LogInformation("Deleted collection '{Collection}'", collection);
    }

    public async Task DeleteDocumentAsync(string collection, Guid documentId, CancellationToken cancellationToken = default)
    {
        if (!await client.CollectionExistsAsync(collection, cancellationToken))
        {
            return;
        }

        await client.DeleteAsync(collection, DocumentFilter(documentId), cancellationToken: cancellationToken);
        logger.LogInformation("Deleted the points of document {DocumentId} from '{Collection}'", documentId, collection);
    }

    public async Task SetTagsAsync(
        string collection,
        Guid documentId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        if (!await client.CollectionExistsAsync(collection, cancellationToken))
        {
            return;
        }

        await client.SetPayloadAsync(
            collection,
            new Dictionary<string, Value> { [TagsField] = tags.ToArray() },
            filter: DocumentFilter(documentId),
            cancellationToken: cancellationToken);

        logger.LogInformation("Retagged document {DocumentId} in '{Collection}' with [{Tags}]",
            documentId, collection, string.Join(", ", tags));
    }

    private static Filter DocumentFilter(Guid documentId) =>
        new() { Must = { Conditions.MatchKeyword(DocumentIdField, documentId.ToString()) } };

    private static string KindValue(DocumentKind kind) => kind.ToString().ToLowerInvariant();

    private static string Payload(MapField<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) ? value.StringValue : string.Empty;

    private static long? IntPayload(MapField<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.IntegerValue
            ? value.IntegerValue
            : null;

    private static IReadOnlyList<string> Tags(ScoredPoint point) =>
        point.Payload.TryGetValue(TagsField, out var value) && value.ListValue is { } list
            ? list.Values.Select(v => v.StringValue).ToList()
            : [];

    private static PrefetchQuery Prefetch(Query query, string? vectorName, Filter? filter, ulong limit)
    {
        var prefetch = new PrefetchQuery { Query = query, Limit = limit };
        if (vectorName is not null)
        {
            prefetch.Using = vectorName;
        }
        if (filter is not null)
        {
            prefetch.Filter = filter;
        }
        return prefetch;
    }

    // "" names the default (unnamed) dense vector when it sits beside named ones.
    private static Vectors HybridVectors(float[] dense, Document keywords) =>
        new Dictionary<string, Vector>
        {
            [""] = dense,
            [KeywordVector] = keywords,
        };

    // Documents and queries must share the language, or their stems won't meet.
    private static Document Keywords(string text, string language) => new()
    {
        Text = text,
        Model = KeywordModel,
        Options =
        {
            ["language"] = language,
            ["avg_len"] = AverageChunkWords,
        },
    };

    // The Qdrant side of a collection is created on its first upsert, once the embedding size is known.
    private async Task EnsureCollectionAsync(string collection, ulong dimensions, CancellationToken cancellationToken)
    {
        if (!dimensionsByCollection.TryGetValue(collection, out var existing) && await client.CollectionExistsAsync(collection, cancellationToken))
        {
            var info = await client.GetCollectionInfoAsync(collection, cancellationToken);
            existing = dimensionsByCollection[collection] = info.Config.Params.VectorsConfig.Params.Size;
        }

        // Qdrant fixes the size when the collection is created, so a new embedding model needs the vectors rebuilt.
        if (existing != 0)
        {
            if (existing != dimensions)
            {
                throw new InvalidOperationException(
                    $"'{collection}' holds {existing}-dimension vectors, but the embedding model gives {dimensions}. " +
                    "Use Re-embed on the collection's page to switch it to the current model.");
            }
            return;
        }

        logger.LogInformation("Creating collection '{Collection}' with {Dimensions} dims, cosine distance, and keyword vectors",
            collection, dimensions);
        await client.CreateCollectionAsync(
            collection,
            new VectorParams { Size = dimensions, Distance = Distance.Cosine },
            sparseVectorsConfig: new SparseVectorConfig
            {
                Map = { [KeywordVector] = new SparseVectorParams { Modifier = Modifier.Idf } },
            },
            cancellationToken: cancellationToken);

        foreach (var field in (string[])[TagsField, DocumentIdField, KindField])
        {
            await client.CreatePayloadIndexAsync(
                collection, field, PayloadSchemaType.Keyword, wait: true, cancellationToken: cancellationToken);
        }

        dimensionsByCollection[collection] = dimensions;
    }
}

public sealed record SearchResults(IReadOnlyList<SearchHit> Hits, ScoreKind ScoreKind);

public enum ScoreKind
{
    // Cosine similarity, from a meaning-only search.
    Similarity,
    // Rank fusion of meaning and keyword matches, which only orders hits.
    Fusion,
    // A reranker's 0–1 judgement of how well the passage answers the query.
    Relevance,
}

public sealed record SearchHit(
    float Score,
    string Content,
    string HeadingPath,
    string Source,
    IReadOnlyList<string> Tags,
    DocumentKind Kind,
    Guid DocumentId,
    long ChunkIndex,
    long? FirstPage,
    long? LastPage);

public sealed record PassageChunk(long ChunkIndex, string Content, string HeadingPath);
