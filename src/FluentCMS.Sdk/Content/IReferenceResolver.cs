namespace FluentCMS.Content;

/// <summary>
/// Resolves the references whose target is not a content type, such as the assets of the media plugin's <c>media</c> field
/// type, for population, for the rich text renderers and for the check of a write. A plugin field type stored as
/// <see cref="Schema.FieldStorageKind.Reference"/> targets its own name, and a rich text <c>media</c> node targets <c>media</c>.
/// A plugin registers one as a scoped service, one per target; without one, a reference stays <c>{ "id", "type" }</c> and a
/// write does not check it.
/// </summary>
public interface IReferenceResolver
{
    /// <summary>The target it resolves, such as <c>media</c>.</summary>
    string TargetType { get; }

    /// <summary>
    /// Resolves targets in the site and for the principal of the current operation. A target the principal may not read,
    /// or that does not exist, is left out, and population then returns it as null, or leaves it out of a list.
    /// </summary>
    /// <param name="ids">The ids, each once.</param>
    /// <param name="locale">The locale of the read.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What population returns for each target the principal may read, by id.</returns>
    Task<IReadOnlyDictionary<Guid, ResolvedReference>> ResolveAsync(IReadOnlyCollection<Guid> ids, string locale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the targets that do not exist, or no longer do, in the site of the current operation, whoever may read them,
    /// so that a write refuses a reference to them with <see cref="ValidationErrorCodes.UnknownReference"/>.
    /// </summary>
    /// <param name="ids">The ids, each once.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The ids that name nothing.</returns>
    Task<IReadOnlySet<Guid>> FindMissingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
