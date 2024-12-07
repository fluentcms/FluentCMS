namespace FluentCMS.Services.MessageHandlers;

public class GlobalSettingsMessageHandler(IGlobalSettingsService globalSettingsService) : IMessageHandler<SetupTemplate>
{
    public async Task Handle(Message<SetupTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupStarted:
                var globalSettings = notification.Payload.GlobalSettings;
                globalSettings.SuperAdmins ??= [notification.Payload.Username];

                await globalSettingsService.Update(globalSettings, cancellationToken);
                break;

            case ActionNames.SetupCompleted:
                break;

            default:
                break;
        }
    }
}
