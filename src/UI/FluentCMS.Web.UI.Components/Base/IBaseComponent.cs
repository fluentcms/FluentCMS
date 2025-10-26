namespace FluentCMS.Web.UI.Components;

public interface IBaseComponent
{
    bool Visible { get; set; }
    string? Class { get; set; }
    string? CssName { get; set; }
    IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
    //string GetDefaultCssName();
}
