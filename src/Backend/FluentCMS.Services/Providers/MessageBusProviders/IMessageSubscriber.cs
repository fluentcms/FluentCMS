namespace FluentCMS.Providers;

public interface IMessageSubscriber<TMessage>
{
    void Subscribe(Func<string, TMessage, Task> onMessageReceived);
}
