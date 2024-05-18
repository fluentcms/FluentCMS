namespace FluentCMS.Web.UI.Components;

public partial class Switch
{
    protected override void OnInitialized()
    {
        base.OnInitialized();

        Id ??= Guid.NewGuid().ToString();
    }
}
