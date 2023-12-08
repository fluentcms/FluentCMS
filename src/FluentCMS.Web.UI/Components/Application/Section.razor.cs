using FluentCMS.Api.Models;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Application;

public partial class Section
{
    [Parameter]
    public string Name { get; set; } = string.Empty;

    [CascadingParameter]
    public AppState? AppState { get; set; }

    public List<PluginResponse> Plugins { get; set; } = [];

    protected override void OnInitialized()
    {
        base.OnInitialized();
    }

    protected override async Task OnInitializedAsync()
    {
        Plugins = GetPlugins();
        await base.OnInitializedAsync();
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
    }

    protected override async Task OnParametersSetAsync()
    {
        Plugins = GetPlugins();
        await base.OnParametersSetAsync();
    }

    private Type? GetType(PluginResponse plugin)
    {
        var assembly = typeof(Section).Assembly;

        if (AppState?.ViewMode?.ToLower() == "edit")
            return assembly.DefinedTypes.FirstOrDefault(x => x.Name == plugin.Definition.EditType);

        return assembly.DefinedTypes.FirstOrDefault(x => x.Name == plugin.Definition.ViewType);
    }

    private List<PluginResponse> GetPlugins()
    {
        if (AppState?.Page?.Plugins == null)
            return [];

        var result = AppState.Page.Plugins.Where(x =>
            x.Section.Equals(Name, StringComparison.CurrentCultureIgnoreCase));

        result = result.Where(x => AppState?.PluginId == null || x.Id == AppState?.PluginId);

        return result.OrderBy(x => x.Order).ToList();
    }
}
