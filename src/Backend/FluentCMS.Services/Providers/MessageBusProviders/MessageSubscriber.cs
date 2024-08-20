namespace FluentCMS.Providers;

public class MessageSubscriber<TMessage> : IMessageSubscriber<TMessage>
{
    private readonly IMessageBus<TMessage> _messageBus;

    public MessageSubscriber(IMessageBus<TMessage> messageBus)
    {
        _messageBus = messageBus;
    }

    public void Subscribe(Func<string, TMessage, Task> onMessageReceived)
    {
        _messageBus.AddSubscriber(onMessageReceived);
    }
}
