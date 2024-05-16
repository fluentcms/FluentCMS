namespace FluentCMS.Web.UI.Components;

public partial class Checkbox
{
    protected override void OnInitialized()
    {
        base.OnInitialized();

        Id ??= Guid.NewGuid().ToString();
    }
}
