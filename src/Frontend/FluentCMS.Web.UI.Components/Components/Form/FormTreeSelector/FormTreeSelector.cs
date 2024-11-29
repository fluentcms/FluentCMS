namespace FluentCMS.Web.UI.Components;

public class TreeSelectorItemType
{
    public string Text { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public List<TreeSelectorItemType> Items { get; set; } = [];
}
