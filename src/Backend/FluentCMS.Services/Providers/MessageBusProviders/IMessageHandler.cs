using MediatR;

namespace FluentCMS.Providers;

public interface IMessageHandler<TPayload> : INotificationHandler<Message<TPayload>>
{
}
