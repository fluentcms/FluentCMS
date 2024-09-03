namespace FluentCMS.Services.Setup.Handlers;

public class GlobalSettingsHandler(IGlobalSettingsService globalSettingsService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.GlobalSettings;

    public override async Task<SetupContext> Handle(SetupContext context)
    {
        context.GlobalSettings.SuperAdmins = new List<string> { context.SuperAdmin.UserName! };

        context.GlobalSettings = await globalSettingsService.Init(context.GlobalSettings);

        return await base.Handle(context);
    }
}
