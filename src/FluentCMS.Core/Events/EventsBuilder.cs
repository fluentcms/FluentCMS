using FluentCMS.Activity;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Events;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

public sealed class EventsBuilder
{
    private readonly IServiceCollection _services;
    private readonly HashSet<Type> _activities = [];

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

    // Writes a row of the activity log for every event of the type, through the outbox so that none is lost. The name is
    // what cms_outbox stores with the row, so it must outlive renames; the action of the entry is a good one.
    public EventsBuilder AddActivity<TEvent>(string name, Func<TEvent, ActivityEntry> describe)
        where TEvent : CmsEvent
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(describe);

        if (!_activities.Add(typeof(TEvent)))
        {
            throw new FluentCmsConfigurationException(ActivityErrorCodes.DuplicateActivity, $"{typeof(TEvent).Name} is already described for the activity log.");
        }

        _services.AddSingleton(new ActivityDescription<TEvent>(describe));
        return AddOutboxHandler<TEvent, ActivityRecorder<TEvent>>("activity." + name);
    }
}
