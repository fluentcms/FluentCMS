namespace FluentCMS.Providers;

public interface IMessageBus<TMessage>
{
    void AddSubscriber(Func<string, TMessage, Task> subscriber);
    IReadOnlyList<Func<string, TMessage, Task>> Subscribers { get; }
}
