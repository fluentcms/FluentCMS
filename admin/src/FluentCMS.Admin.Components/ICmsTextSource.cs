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
    /// <param name="name">
    /// The name of the text: a name in the resources <c>FluentCMS.Admin.Components.Resources.CmsStrings</c>, or a key of the
    /// library under the set, most of which the resources do not hold.
    /// </param>
    /// <returns>
    /// The text, or <see langword="null"/> for a name the resources do not hold, never the name itself, so that the library's
    /// own text is shown for its keys.
    /// </returns>
    string? GetText(string name);
}
