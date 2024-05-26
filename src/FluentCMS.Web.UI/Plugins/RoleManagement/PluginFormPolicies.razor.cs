namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class PluginFormPolicies 
{
    [Parameter]
    public bool Disabled { get; set; }   

    [Parameter]
    public string? Label { get; set; }   

    [Parameter]
    public List<Policy> Policies { get; set; } = [];   

    [Parameter]
    public List<Policy> Value { get; set; } = [];

}
