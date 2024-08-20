namespace FluentCMS.Providers;

public class MessageBus<TMessage> : IMessageBus<TMessage>
{
    private readonly List<Func<string, TMessage, Task>> _subscribers;

    public MessageBus()
    {
        _subscribers = new List<Func<string, TMessage, Task>>();
    }

    public IReadOnlyList<Func<string, TMessage, Task>> Subscribers => _subscribers;

    public void AddSubscriber(Func<string, TMessage, Task> subscriber)
    {
        _subscribers.Add(subscriber);
    }
}
