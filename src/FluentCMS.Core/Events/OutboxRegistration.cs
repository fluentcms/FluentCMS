using System.Collections.Frozen;
using System.Text.Json;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Events;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

internal sealed class OutboxRegistration
{
    private OutboxRegistration(string name, Type eventType, Type handler, Func<string, CmsEvent?> read, Func<IServiceProvider, CmsEvent, EventDelivery, CancellationToken, ValueTask> invoke, Func<bool>? active)
    {
        Active = active ?? (() => true);
        Name = name;
        EventType = eventType;
        Handler = handler;
        Read = read;
        Invoke = invoke;
    }

    public string Name { get; }

    // Whether new events are recorded for it. A registration that is not active is still found for the rows already recorded.
    public Func<bool> Active { get; }

    public Type EventType { get; }

    public Type Handler { get; }

    public Func<string, CmsEvent?> Read { get; }

    public Func<IServiceProvider, CmsEvent, EventDelivery, CancellationToken, ValueTask> Invoke { get; }

    public static OutboxRegistration Create<TEvent, THandler>(string name, Func<bool>? active = null)
        where TEvent : CmsEvent
        where THandler : class, IOutboxEventHandler<TEvent> =>
        new(
            name,
            typeof(TEvent),
            typeof(THandler),
            json => JsonSerializer.Deserialize<TEvent>(json, EventJson.Options),
            (services, @event, delivery, cancellationToken) => services.GetRequiredService<THandler>().HandleAsync((TEvent)@event, delivery, cancellationToken),
            active);
}

internal static class EventJson
{
    public static readonly JsonSerializerOptions Options = JsonSerializerOptions.Web;
}

internal sealed class OutboxRegistry
{
    private readonly FrozenDictionary<string, OutboxRegistration> _byName;
    private readonly FrozenDictionary<Type, OutboxRegistration[]> _byEvent;

    public OutboxRegistry(IEnumerable<OutboxRegistration> registrations, IHookTiebreaker? tiebreaker = null)
    {
        var all = registrations.ToList();
        var duplicate = all.GroupBy(registration => registration.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new FluentCmsConfigurationException(EventErrorCodes.DuplicateOutboxHandler, $"The outbox handler '{duplicate.Key}' is registered {duplicate.Count()} times.");
        }

        _byName = all.ToFrozenDictionary(registration => registration.Name, StringComparer.Ordinal);
        _byEvent = all
            .GroupBy(registration => registration.EventType)
            .ToFrozenDictionary(group => group.Key, group => HandlerOrder.Sort(group, registration => registration.Handler, tiebreaker).ToArray());
    }

    public bool Any => _byName.Values.Any(registration => registration.Active());

    public OutboxRegistration? Find(string name) => _byName.GetValueOrDefault(name);

    public IReadOnlyList<OutboxRegistration> ForEvent(Type eventType) =>
        _byEvent.GetValueOrDefault(eventType)?.Where(registration => registration.Active()).ToArray() ?? [];
}
