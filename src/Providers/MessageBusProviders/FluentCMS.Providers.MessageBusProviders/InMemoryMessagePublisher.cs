using MediatR;

namespace FluentCMS.Providers.MessageBusProviders;

public class InMemoryMessagePublisher(IMediator mediator) : IMessagePublisher
{
    public async Task Publish<TPayload>(Message<TPayload> message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await mediator.Publish(message, cancellationToken);
    }
}
