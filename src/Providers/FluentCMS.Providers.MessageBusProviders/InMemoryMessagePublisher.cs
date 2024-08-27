using MediatR;

namespace FluentCMS.Providers.MessageBusProviders;

public class InMemoryMessagePublisher(IMediator mediator) : IMessagePublisher
{
    public async Task Publish<TPayload>(Message<TPayload> message, CancellationToken cancellationToken = default)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        await mediator.Publish(message, cancellationToken);
    }
}
