using FluentCMS.Activity;
using FluentCMS.Authorization;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins;

/// <summary>
/// What a plugin adds to while the host is set up, in <see cref="IPlugin.ConfigureServices"/>. Core implements it; a plugin
/// only calls it. Tables and endpoints are added through the extension methods of <c>FluentCMS.Plugins.Abstractions</c>.
/// </summary>
public interface IPluginServiceBuilder
{
    /// <summary>The name of the plugin, from its <see cref="FluentCmsPluginAttribute"/>.</summary>
    string Name { get; }

    /// <summary>
    /// The services of the host. A service the plugin adds here is added before those of core, so it replaces a core service
    /// that core adds only when none is there.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The provider slots, to replace one such as <see cref="Providers.IEmailSender"/> whatever the order of registration.
    /// The host's choice wins over a plugin's, and two plugins that replace one slot stop the host unless the host chooses.
    /// </summary>
    Providers.IProviderSlots Providers { get; }

    /// <summary>
    /// Adds a field type. Its name must not be one of core's or another plugin's. A setting of kind
    /// <see cref="FieldSettingKind.FieldReference"/> must name a sibling field, and is optional.
    /// </summary>
    /// <typeparam name="TFieldType">The field type, created once with its parameterless constructor.</typeparam>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddFieldType<TFieldType>()
        where TFieldType : class, IFieldType, new();

    /// <summary>Adds permissions to the catalogue that roles grant from.</summary>
    /// <typeparam name="TProvider">The provider of the permissions, a singleton.</typeparam>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddPermissions<TProvider>()
        where TProvider : class, IPermissionProvider;

    /// <summary>
    /// Adds a settings class, read through <see cref="Settings.ISettings{T}"/> and served by the Management API under
    /// <c>/settings/{area}</c>. Its permission <c>settings.{area}.manage</c> joins the catalogue.
    /// </summary>
    /// <typeparam name="TSettings">A class with <see cref="Settings.SettingsAttribute"/> and a parameterless constructor.</typeparam>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddSettings<TSettings>()
        where TSettings : class, new();

    /// <summary>
    /// Declares a provider slot that the plugin owns, such as the media plugin's file storage, filled with a singleton of
    /// <typeparamref name="TDefault"/> unless the host or another plugin replaces it through <see cref="Providers"/>. The
    /// startup report lists it with core's slots. A slot that core or another plugin declares already stops the host.
    /// </summary>
    /// <typeparam name="TSlot">The slot, an interface or class of the SDK or of the plugin.</typeparam>
    /// <typeparam name="TDefault">The class that fills it by default.</typeparam>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddProviderSlot<TSlot, TDefault>()
        where TSlot : class
        where TDefault : class, TSlot;

    /// <summary>Adds a job that the runner executes.</summary>
    /// <typeparam name="TJob">The job, created per attempt.</typeparam>
    /// <typeparam name="TPayload">Its payload.</typeparam>
    /// <param name="type">What the job table stores, so it must outlive renames of the class.</param>
    /// <param name="attemptTimeout">The limit of one attempt, or null for the runner's.</param>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddJob<TJob, TPayload>(string type, TimeSpan? attemptTimeout = null)
        where TJob : class, IJob<TPayload>;

    /// <summary>
    /// Adds a job queue with workers on every instance, so that the plugin's jobs, enqueued with <see cref="JobOptions.Queue"/>,
    /// hold up no other job. A worker count that the host sets for the queue, in code or under <c>FluentCms:Jobs:Queues</c>,
    /// wins over the plugin's.
    /// </summary>
    /// <param name="name">The name of the queue, at most 64 characters.</param>
    /// <param name="workers">The workers of the queue on this instance, read once from the host's services when the job runner's options are built.</param>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddJobQueue(string name, Func<IServiceProvider, int> workers);

    /// <summary>Adds a handler that hears events of one type in this process, after the commit.</summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <typeparam name="THandler">The handler, scoped.</typeparam>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddEventHandler<TEvent, THandler>()
        where TEvent : CmsEvent
        where THandler : class, IEventHandler<TEvent>;

    /// <summary>Adds a handler that gets every event of one type through the outbox, so that none is lost.</summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <typeparam name="THandler">The handler, scoped.</typeparam>
    /// <param name="name">What the outbox stores, so it must outlive renames of the class.</param>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddOutboxHandler<TEvent, THandler>(string name)
        where TEvent : CmsEvent
        where THandler : class, IOutboxEventHandler<TEvent>;

    /// <summary>Writes a row of the activity log for every event of one type, through the outbox.</summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="name">What the outbox stores with the row, so it must outlive renames.</param>
    /// <param name="describe">Describes the row of an event.</param>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddActivity<TEvent>(string name, Func<TEvent, ActivityEntry> describe)
        where TEvent : CmsEvent;

    /// <summary>
    /// Adds an error code that the plugin raises, so that a <see cref="PluginException"/> or a rejection with it is answered
    /// with its status and title. A code is upper case letters followed by four digits, such as <c>ACME0001</c>, and must not
    /// be one that core or another plugin has.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="status">The HTTP status a client sees, from 400 to 599.</param>
    /// <param name="title">A short title for people.</param>
    /// <returns>This builder.</returns>
    IPluginServiceBuilder AddErrorCode(string code, int status, string title);
}
