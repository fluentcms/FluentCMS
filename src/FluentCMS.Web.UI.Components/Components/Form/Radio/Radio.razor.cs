namespace FluentCMS.Web.UI.Components;

public partial class Radio<TValue>
{
    public override string GetDefaultCSSName()
    {
        return "Radio";
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        Id ??= Guid.NewGuid().ToString();
    }
}
