namespace FluentCMS.Admin.Components;

/// <summary>Reads the texts the wrapped set shows itself, such as the announcement of a grid's sort or a row's selection state.</summary>
/// <remarks>
/// <see cref="Microsoft.Extensions.DependencyInjection.CmsComponentsServiceCollectionExtensions.AddCmsComponents"/> registers
/// one that reads the set's resources in the current UI culture. A host that localizes through its own localizer replaces it,
/// so that the set's texts take the same path as the host's.
/// </remarks>
public interface ICmsTextSource
{
    /// <summary>Returns a text of the wrapped set in the interface culture.</summary>
    /// <param name="name">The name of the text in the resources <c>FluentCMS.Admin.Components.Resources.CmsStrings</c>.</param>
    /// <returns>The text, or <see langword="null"/> when the resources have none of that name.</returns>
    string? GetText(string name);
}
