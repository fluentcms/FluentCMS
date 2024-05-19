namespace FluentCMS.Web.UI.Components;

public partial class Checkbox
{
    public override string GetDefaultCSSName()
    {
        return "checkbox";
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        Id ??= Guid.NewGuid().ToString();
    }
}
