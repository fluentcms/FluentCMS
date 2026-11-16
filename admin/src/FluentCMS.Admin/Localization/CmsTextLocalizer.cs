using FluentCMS.Admin.Components;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Localization;

// The wrapped set's texts read through the admin's localizer, from the set's own resources, so that they take the same path
// as the admin's (and the pseudo-cultures of the tests see them).
internal sealed class CmsTextLocalizer(IStringLocalizerFactory factory) : ICmsTextSource
{
    private readonly IStringLocalizer localizer = factory.Create("Resources.CmsStrings", typeof(ICmsTextSource).Assembly.GetName().Name!);

    public string? GetText(string name) => localizer[name] is { ResourceNotFound: false } text ? text.Value : null;
}
