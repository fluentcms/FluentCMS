using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Desk;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Media;
using FluentCMS.Core.Models;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Core;

public static class CoreServiceCollectionExtensions
{
    // The composition seam for domain and application services; each epic adds its registrations here.
    public static IServiceCollection AddFluentCmsCore(this IServiceCollection services)
    {
        // Built-ins under FluentCMS.Core.Builtin are discovered the same way a plugin assembly would be (EXT-02).
        services.AddFluentCmsExtensions(typeof(CoreServiceCollectionExtensions).Assembly);

        services.TryAddSingleton<FieldTypeRegistry>();
        services.TryAddSingleton<CheckTypeRegistry>();
        services.TryAddSingleton<MediaStorageRegistry>();

        // Reads through IAuditReader, which Infrastructure provides.
        services.AddScoped<IAuditExporter, AuditExporter>();

        // Reads through IContentTypeDirectory, which Infrastructure provides.
        services.AddScoped<ISchemaDocuments, SchemaDocuments>();

        // Reads through IAgentCredentialDirectory and IAgentActivity, which Infrastructure provides.
        services.AddScoped<IAgentPermissions, AgentPermissions>();

        // Reads through IModelProfiles, IModelClients, IModelCallLog and IOutboundHosts, which Infrastructure provides (ADR-0008).
        services.AddScoped<IModelGateway, ModelGateway>();

        // Reads the active checks through ICheckDirectory, which Infrastructure provides.
        services.AddScoped<ICheckRunner, CheckRunner>();

        // Reads through IChangeSetLists and IEntityFacts, which Infrastructure provides.
        services.AddScoped<IDeskQueues, DeskQueues>();

        // The handlers that run as an engine; a job worker runs any other handler as whoever enqueued its job (ADR-0012).
        services.AddSingleton(new EngineJobs(new Dictionary<Type, SystemInitiator>()));

        return services;
    }
}
