﻿namespace FluentCMS.Web.Api.Models;

public record PageFullDetailModel(
    Page page,
    string path,
    Site site,
    IEnumerable<Layout> layouts,
    IEnumerable<Plugin> plugins,
    Dictionary<Guid, PluginDefinition> pluginDefinitions,
    bool forceMainSection)
{

}
