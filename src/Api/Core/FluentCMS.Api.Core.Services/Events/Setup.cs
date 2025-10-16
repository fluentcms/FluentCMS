namespace FluentCMS.Api.Core.Services.Events;

public class SetupStartedEvent(SetupTemplate setupTemplate) : EventBase
{
    public SetupTemplate SetupTemplate { get; } = setupTemplate;
}
