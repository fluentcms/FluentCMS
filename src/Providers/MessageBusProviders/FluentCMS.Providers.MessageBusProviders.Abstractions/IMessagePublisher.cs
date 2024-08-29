namespace FluentCMS.Providers.MessageBusProviders;

public interface IMessagePublisher
{
    Task Publish<TPayload>(Message<TPayload> message, CancellationToken cancellationToken = default);
}
