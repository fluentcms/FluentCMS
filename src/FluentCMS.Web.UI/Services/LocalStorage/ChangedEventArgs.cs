using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Services.LocalStorage;

[ExcludeFromCodeCoverage]
public class ChangedEventArgs
{
    public string Key { get; set; }
    public object OldValue { get; set; }
    public object NewValue { get; set; }
}
