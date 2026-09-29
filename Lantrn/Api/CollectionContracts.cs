using System.ComponentModel.DataAnnotations;
using Lantrn.Services;

namespace Lantrn.Api;

/// <summary>A collection: a separately searchable set of documents.</summary>
/// <param name="Id">The collection's id, used in every collection URL.</param>
/// <param name="Name">Lowercase letters, digits, '-' and '_'. Unique per owner, so other owners may use the same name.</param>
/// <param name="Description">What the collection holds, if one was given.</param>
/// <param name="IsPrivate">Whether only its owner and admins can search it. Public collections are open to every user, and to guests while public search is on.</param>
/// <param name="CreatedAt">When the collection was created.</param>
/// <param name="DocumentCount">How many documents the collection holds.</param>
/// <param name="ChunkCount">How many searchable chunks those documents were split into.</param>
/// <param name="LastIngestedAt">When a document was last added or refreshed, or null while the collection is empty.</param>
public sealed record CollectionResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    int DocumentCount,
    int ChunkCount,
    DateTimeOffset? LastIngestedAt)
{
    internal static CollectionResponse From(CollectionSummary collection) => new(
        collection.Id,
        collection.Name,
        collection.Description,
        collection.IsPrivate,
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
    /// Unique among the collections of the key's user.
    /// </summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9_-]{0,62}$",
        ErrorMessage = "Use lowercase letters, digits, '-' and '_' (starting with a letter or digit), at most 63 characters.")]
    public required string Name { get; init; }

    /// <summary>What the collection holds, shown next to its name.</summary>
    [MaxLength(500)]
    public string? Description { get; init; }

    /// <summary>Only its owner and admins can search a private collection. Public by default.</summary>
    public bool IsPrivate { get; init; }
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
