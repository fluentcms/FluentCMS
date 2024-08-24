namespace FluentCMS.Providers;

public interface IMessagePublisher<TMessage>
{
    Task Publish(string actionName, TMessage messsage);
}
