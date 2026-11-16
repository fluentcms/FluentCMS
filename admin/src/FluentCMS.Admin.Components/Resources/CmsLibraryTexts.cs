using Microsoft.FluentUI.AspNetCore.Components;

namespace FluentCMS.Admin.Components.Resources;

// The texts the library shows inside the wrapped components, such as a row's selection state, read from the set's resources
// under the library's own key, so that they follow the admin's culture. A key the resources lack keeps the library's English.
// As the library does, a text is formatted only when there are arguments, so a translation with a brace and none does not throw.
internal sealed class CmsLibraryTexts(ICmsTextSource texts) : IFluentLocalizer
{
    public string this[string key, params object[] arguments] => texts.GetText(key) switch
    {
        null => ((IFluentLocalizer)this).GetDefault(key, arguments),
        var text when arguments is { Length: > 0 } => CmsStrings.Format(text, arguments),
        var text => text,
    };
}
