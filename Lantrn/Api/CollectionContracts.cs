using System.ComponentModel.DataAnnotations;
using Lantrn.Services;

namespace Lantrn.Api;

/// <summary>A collection: a separately searchable set of documents.</summary>
/// <param name="Name">The collection's key: lowercase letters, digits, '-' and '_'.</param>
/// <param name="Description">What the collection holds, if one was given.</param>
/// <param name="CreatedAt">When the collection was created.</param>
/// <param name="DocumentCount">How many documents the collection holds.</param>
/// <param name="ChunkCount">How many searchable chunks those documents were split into.</param>
/// <param name="LastIngestedAt">When a document was last added or refreshed, or null while the collection is empty.</param>
public sealed record CollectionResponse(
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    int DocumentCount,
    int ChunkCount,
    DateTimeOffset? LastIngestedAt)
{
    internal static CollectionResponse From(CollectionSummary collection) => new(
        collection.Name,
        collection.Description,
        ApiTime.Utc(collection.CreatedAt),
        collection.DocumentCount,
        collection.ChunkCount,
        ApiTime.Utc(collection.LastIngestedAt));
}

/// <summary>A new, empty collection.</summary>
public sealed record CreateCollectionRequest
{
    /// <summary>
    /// Lowercase letters, digits, '-' and '_', starting with a letter or digit; at most 63 characters.
    /// The name can't be changed later.
    /// </summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9_-]{0,62}$",
        ErrorMessage = "Use lowercase letters, digits, '-' and '_' (starting with a letter or digit), at most 63 characters.")]
    public required string Name { get; init; }

    /// <summary>What the collection holds, shown next to its name.</summary>
    [MaxLength(500)]
    public string? Description { get; init; }
}

/// <summary>A tag and how many documents in the collection carry it.</summary>
/// <param name="Name">The tag, in lowercase.</param>
/// <param name="Documents">How many documents carry it.</param>
public sealed record TagResponse(string Name, int Documents);

// The database hands dates back without a kind; they are always stored in UTC.
internal static class ApiTime
{
    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static DateTimeOffset? Utc(DateTime? value) => value is { } set ? Utc(set) : null;
}
