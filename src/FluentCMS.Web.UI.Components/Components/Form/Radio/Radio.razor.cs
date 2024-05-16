namespace FluentCMS.Web.UI.Components;

public partial class Radio<TValue>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();

        Id ??= Guid.NewGuid().ToString();
    }
}
