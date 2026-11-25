using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Localization;

// The resources a team's app gives AddFluentCmsAdmin<TResources>, which name its content types (spec 3.2, 4.17).
internal sealed record AdminAppResources(Type Source);

// The admin's texts, with the label of a content type, ContentType.{name}, read from the app's resources first, so that a
// team names its types without a satellite assembly of the admin's package.
internal sealed class AdminTextLocalizer : IStringLocalizer<AdminResources>
{
    private const string ContentTypePrefix = "ContentType.";

    private readonly IStringLocalizer admin;
    private readonly IStringLocalizer? app;

    public AdminTextLocalizer(IStringLocalizerFactory factory, IEnumerable<AdminAppResources> appResources)
    {
        ArgumentNullException.ThrowIfNull(factory);
        admin = factory.Create(typeof(AdminResources));
        app = appResources.LastOrDefault() is { } resources ? factory.Create(resources.Source) : null;
    }

    public LocalizedString this[string name] => App(name) is { ResourceNotFound: false } text ? text : admin[name];

    public LocalizedString this[string name, params object[] arguments] =>
        App(name) is { ResourceNotFound: false } ? app![name, arguments] : admin[name, arguments];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => admin.GetAllStrings(includeParentCultures);

    private LocalizedString? App(string name) =>
        app is not null && name.StartsWith(ContentTypePrefix, StringComparison.Ordinal) ? app[name] : null;
}
