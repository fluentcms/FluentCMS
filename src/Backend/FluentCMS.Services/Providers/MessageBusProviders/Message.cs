using MediatR;

namespace FluentCMS.Providers;

public class Message<T>(string action, T payload) : INotification
{
    public string Action { get; } = action;
    public T Payload { get; } = payload;
}
