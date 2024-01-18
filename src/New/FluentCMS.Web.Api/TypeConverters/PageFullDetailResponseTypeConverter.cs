namespace FluentCMS.Web.Api.TypeConverters;

public class PageFullDetailResponseTypeConverter : ITypeConverter<(Page page,
    string path,
    Site site,
    IEnumerable<Layout> layouts,
    IEnumerable<Plugin> plugins,
    Dictionary<Guid, PluginDefinition> pluginDefinitions,
    bool forceMainSection), PageFullDetailResponse>
{
    public PageFullDetailResponse Convert(
    (Page page,
    string path,
    Site site,
    IEnumerable<Layout> layouts,
    IEnumerable<Plugin> plugins,
    Dictionary<Guid, PluginDefinition> pluginDefinitions,
    bool forceMainSection) source,
    PageFullDetailResponse destination,
    ResolutionContext context)
    {
        destination = destination ?? new PageFullDetailResponse();
        destination = context.Mapper.Map(source.page, destination);
        destination.FullPath = source.path;
        destination.Site = context.Mapper.Map<Site, SiteDetailResponse>(source.site);

        if (source.page.LayoutId.HasValue)
        {
            var layout = source.layouts.Where(l => l.Id == source.page.LayoutId.Value).First();
            destination.Layout = context.Mapper.Map<Layout, LayoutDetailResponse>(layout);
        }
        else
        {
            var layout = source.layouts.Where(l => l.IsDefault).First();
            destination.Layout = context.Mapper.Map<Layout, LayoutDetailResponse>(layout);
        }

        destination.Sections = new Dictionary<string, List<PluginDetailResponse>>();
        foreach (var plugin in source.plugins)
        {
            if (!destination.Sections.ContainsKey(plugin.Section))
                destination.Sections.Add(plugin.Section, new List<PluginDetailResponse>());

            var pluginResponse = context.Mapper.Map<Plugin, PluginDetailResponse>(plugin);
            pluginResponse.Definition = context.Mapper.Map<PluginDefinition, PluginDefinitionDetailResponse>(source.pluginDefinitions[plugin.DefinitionId]);
            destination.Sections[source.forceMainSection ? "Main" : plugin.Section].Add(pluginResponse);
        }
        return destination;
    }
}
