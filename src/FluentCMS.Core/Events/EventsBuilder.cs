using FluentCMS.Events;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

public sealed class EventsBuilder
{
    private readonly IServiceCollection _services;

    internal EventsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public OutboxOptions Outbox { get; } = new();

    public EventsBuilder AddHandler<TEvent, THandler>()
        where TEvent : CmsEvent
        where THandler : class, IEventHandler<TEvent>
    {
        _services.AddScoped<IEventHandler<TEvent>, THandler>();
        return this;
    }

    // The name is what cms_outbox stores, so it must outlive renames of the class.
    public EventsBuilder AddOutboxHandler<TEvent, THandler>(string name)
        where TEvent : CmsEvent
        where THandler : class, IOutboxEventHandler<TEvent>
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, OutboxWriter.HandlerMaxLength, nameof(name));

        _services.AddScoped<THandler>();
        _services.AddSingleton(OutboxRegistration.Create<TEvent, THandler>(name));
        return this;
    }
}
