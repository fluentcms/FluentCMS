namespace FluentCMS.Api.Core.Events;

public class ApiTokenAddedEvent(ApiToken apiToken) : EventBase
{
    public ApiToken ApiToken { get; } = apiToken;
}

public class ApiTokenUpdatedEvent(ApiToken apiToken) : EventBase
{
    public ApiToken ApiToken { get; } = apiToken;
}

public class ApiTokenRemovedEvent(ApiToken apiToken) : EventBase
{
    public ApiToken ApiToken { get; } = apiToken;
}

public class ApiTokenSecretRegeneratedEvent(ApiToken apiToken) : EventBase
{
    public ApiToken ApiToken { get; } = apiToken;
}
