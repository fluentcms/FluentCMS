namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class PluginFormPolicies 
{
    [Parameter]
    public bool Disabled { get; set; }   

    [Parameter]
    public string? Label { get; set; }   

    [Parameter]
    public ICollection<Policy> Policies { get; set; } = [];   

    [Parameter]
    public ICollection<Policy> Value { get; set; } = [];

    private Task OnChange(Policy policy)
    {
        Value = Policies.Where(x => x.Selected).ToList();
        return Task.CompletedTask;
    }

}
