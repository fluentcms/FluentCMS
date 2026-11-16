using Microsoft.FluentUI.AspNetCore.Components;

namespace FluentCMS.Admin.Components.Resources;

// The texts the library shows inside the wrapped components, such as a row's selection state, read from the set's resources
// under the library's own key, so that they follow the admin's culture. A key the resources lack keeps the library's English.
internal sealed class CmsLibraryTexts(ICmsTextSource texts) : IFluentLocalizer
{
    public string this[string key, params object[] arguments] =>
        texts.GetText(key) is { } text ? CmsStrings.Format(text, arguments) : ((IFluentLocalizer)this).GetDefault(key, arguments);
}
