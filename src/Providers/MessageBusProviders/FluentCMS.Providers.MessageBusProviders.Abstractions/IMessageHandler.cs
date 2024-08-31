using MediatR;

namespace FluentCMS.Providers.MessageBusProviders;

public interface IMessageHandler<TPayload> : INotificationHandler<Message<TPayload>>
{
}
