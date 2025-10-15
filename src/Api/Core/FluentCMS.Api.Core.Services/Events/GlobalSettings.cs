namespace FluentCMS.Api.Core.Services.Events;

public class GlobalSettingsUpdatedEvent(GlobalSettings settings) : EventBase
{
    public GlobalSettings Settings { get; } = settings;
}
