namespace FluentCMS.Providers;

public class MessagePublisher<TMessage> : IMessagePublisher<TMessage>
{
    private readonly IMessageBus<TMessage> _messageBus;

    public MessagePublisher(IMessageBus<TMessage> messageBus)
    {
        _messageBus = messageBus;
    }

    public async Task Publish(string actionName, TMessage messsage)
    {
        var subscirbers = _messageBus.Subscribers;
        foreach (var subscriber in subscirbers)
            await subscriber(actionName, messsage);
    }
}
