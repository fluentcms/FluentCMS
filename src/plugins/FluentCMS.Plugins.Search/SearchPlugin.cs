using FluentCMS.Events;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Search;
using FluentCMS.Providers;
using Microsoft.Extensions.DependencyInjection;

[assembly: FluentCmsPlugin(typeof(SearchPlugin))]

namespace FluentCMS.Plugins.Search;

// Full-text search of spec 5.7: the ISearchIndex slot, over an index of the fields marked Searchable that the handlers
// of the entry events keep current through the outbox, and that the job search.rebuild fills for content that was there
// before.
[FluentCmsPlugin(Name, SdkRange = "[1.0.0,2.0.0)")]
public sealed class SearchPlugin : IPlugin
{
    public const string Name = "search";
    public const string Prefix = "/search";

    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        plugin.Services.AddScoped<SearchStore>();
        plugin.Services.AddScoped<SearchIndexer>();
        plugin.Services.AddScoped<SearchService>();
        plugin.Providers.Use<ISearchIndex, SearchIndex>();

        plugin
            .AddPermissions<SearchPermissions>()
            .AddModel<SearchModel>()
            .AddMigrations(typeof(SearchPlugin).Assembly)
            .AddJob<SearchRebuildJob, SearchRebuildPayload>(SearchRebuildJob.Type, SearchRebuildJob.AttemptTimeout)
            .AddOutboxHandler<SiteDeletedEvent, DeletedSiteSearch>("search.cleanup.site.deleted")
            .AddOutboxHandler<PluginEnabledEvent, EnabledSiteSearch>("search.rebuild.plugin.enabled")
            .AddErrorCode(SearchErrorCodes.NotSearchable, 400, "Type not searchable")
            .AddErrorCode(SearchErrorCodes.InvalidRebuild, 422, "Invalid search rebuild");

        Index<EntryDraftSavedEvent>(plugin, "draft_saved");
        Index<EntryPublishedEvent>(plugin, "published");
        Index<EntryUnpublishedEvent>(plugin, "unpublished");
        Index<EntryDraftDiscardedEvent>(plugin, "draft_discarded");
        Index<EntryVersionRestoredEvent>(plugin, "version_restored");
        Index<EntryTrashedEvent>(plugin, "trashed");
        Index<EntryRestoredEvent>(plugin, "restored");
        Index<EntryPurgedEvent>(plugin, "purged");
    }

    public void Configure(IPluginAppBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapManagement(Prefix).MapSearchEndpoints();
    }

    private static void Index<TEvent>(IPluginServiceBuilder plugin, string name)
        where TEvent : EntryEvent =>
        plugin.AddOutboxHandler<TEvent, SearchEventHandler<TEvent>>("search.content." + name);
}
