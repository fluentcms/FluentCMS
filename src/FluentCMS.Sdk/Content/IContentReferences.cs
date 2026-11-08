namespace FluentCMS.Content;

/// <summary>
/// The references that entries hold to targets outside content, such as the assets of the media plugin (<c>media</c>), as
/// <c>cms_reference</c> records them for fields, blocks and rich text, in every site. Entries in the trash count only where
/// a member says so. A content type is no target here: relations between entries are the content service's.
/// </summary>
public interface IContentReferences
{
    /// <summary>
    /// Lists the references to a target from entries outside the trash, in their draft and published states, leaving out
    /// those of entries, locales and fields the principal of the operation may not read.
    /// </summary>
    /// <param name="targetType">The kind of target, such as <c>media</c>.</param>
    /// <param name="targetId">The id of the target.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The references, by type, entry, state, locale and field.</returns>
    /// <exception cref="ArgumentException"><paramref name="targetType"/> is empty or a content type.</exception>
    Task<IReadOnlyList<ContentReference>> FindAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default);

    /// <summary>Counts the entries outside the trash that refer to a target, whatever the principal may read. It checks no permission.</summary>
    /// <param name="targetType">The kind of target, such as <c>media</c>.</param>
    /// <param name="targetId">The id of the target.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The number of entries.</returns>
    /// <exception cref="ArgumentException"><paramref name="targetType"/> is empty or a content type.</exception>
    Task<int> CountAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every reference to a target, from both states of every entry, those in the trash included, so that a field
    /// that held it reads as empty. Rich text keeps its own copy of the reference. It checks no permission and writes no
    /// version; the cached Delivery reads of those entries are evicted, with a <see cref="Events.CacheInvalidatedEvent"/>.
    /// </summary>
    /// <param name="targetType">The kind of target, such as <c>media</c>.</param>
    /// <param name="targetId">The id of the target.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The number of references removed.</returns>
    /// <exception cref="ArgumentException"><paramref name="targetType"/> is empty or a content type.</exception>
    Task<int> RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default);
}

/// <summary>A reference from a field of an entry to a target outside content.</summary>
/// <param name="Type">The content type of the entry.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Status">The state of the entry that holds it.</param>
/// <param name="Locale">The locale of a localized value, or <see langword="null"/>.</param>
/// <param name="Field">The path of the field, as queries name it.</param>
public sealed record ContentReference(string Type, Guid EntryId, QueryStatus Status, string? Locale, string Field);
