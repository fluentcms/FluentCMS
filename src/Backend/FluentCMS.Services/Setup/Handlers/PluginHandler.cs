namespace FluentCMS.Services.Setup.Handlers;

public class PluginHandler(IPluginDefinitionService pluginDefinitionService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.Plugin;

    public override async Task<SetupContext> Handle(SetupContext context)
    {
        foreach (var pluginDefTemplate in context.AdminTemplate.PluginDefinitions)
        {
            var pluginDef = new PluginDefinition
            {
                Name = pluginDefTemplate.Name,
                Description = pluginDefTemplate.Description,
                Types = pluginDefTemplate.Types,
                Assembly = pluginDefTemplate.Assembly,
                Locked = pluginDefTemplate.Locked,
                Category = pluginDefTemplate.Category
            };
            context.PluginDefinitions.Add(await pluginDefinitionService.Create(pluginDef));
        }

        return await base.Handle(context);
    }
}
