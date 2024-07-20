namespace FluentCMS.Web.UI;

public partial class PluginsSection
{
    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    private string GetStyles(Dictionary<string, string> styles)
    {
        var result = "";

        foreach (var key in styles.Keys)
        {
            if (char.IsUpper(key[0]))
                continue;
            result += key + ": " + styles[key] + "; ";
        }

        return result.Trim();
    }

    private string? GetStyleValue(Dictionary<string, string> styles, string key)
    {
        
        if(styles.ContainsKey(key))
        {
            return styles[key];
        }

        return null;
    }

    private bool IsFullWidth(Dictionary<string, string> styles)
    {
        object value = null;
        if(styles.ContainsKey("FullWidth")) {

            value = styles["FullWidth"];
        }

        if(value is null)
        {
            return false;
        }
        else
        {
            return true;
        }
    }
}
