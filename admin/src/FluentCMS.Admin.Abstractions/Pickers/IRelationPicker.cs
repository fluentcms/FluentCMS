using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Pickers;

/// <summary>Lets the user choose entries of a content type in a dialog over the type's list.</summary>
public interface IRelationPicker
{
    /// <summary>Opens the picker.</summary>
    /// <param name="request">What to choose.</param>
    /// <param name="cancellationToken">Closes the picker without a choice.</param>
    /// <returns>
    /// The chosen entries in the order they were chosen, one that the picker did not read with its id only; or
    /// <see langword="null"/> when the user closed the picker without a choice.
    /// </returns>
    Task<IReadOnlyList<ContentEntry>?> PickAsync(RelationPickerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What an <see cref="IRelationPicker"/> lets the user choose.</summary>
public sealed class RelationPickerRequest
{
    /// <summary>Gets the name of the content type to choose entries of.</summary>
    public required string Type { get; init; }

    /// <summary>Gets whether the user may choose several entries.</summary>
    public bool Multiple { get; init; }

    /// <summary>Gets the ids chosen when the picker opens, in their order.</summary>
    public IReadOnlyList<Guid> Selected { get; init; } = [];

    /// <summary>Gets the content locale the entries are read in; the view's when <see langword="null"/>.</summary>
    public string? Locale { get; init; }
}
