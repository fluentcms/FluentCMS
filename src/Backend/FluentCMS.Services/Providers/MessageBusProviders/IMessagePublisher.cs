namespace FluentCMS.Providers;

public interface IMessagePublisher
{
    Task Publish<TPayload>(Message<TPayload> message, CancellationToken cancellationToken = default);
}
