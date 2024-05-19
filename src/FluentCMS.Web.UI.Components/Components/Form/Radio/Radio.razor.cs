namespace FluentCMS.Web.UI.Components;

public partial class Radio<TValue>
{
    public override string GetDefaultCSSName()
    {
        return "radio";
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        Id ??= Guid.NewGuid().ToString();
    }
}
