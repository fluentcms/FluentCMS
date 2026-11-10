using FluentCMS.Content;
using FluentCMS.Plugins;
using FluentCMS.Plugins.GraphQL;
using FluentCMS.Schema;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.CostAnalysis;
using HotChocolate.Execution;
using HotChocolate.PersistedOperations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

[assembly: FluentCmsPlugin(typeof(GraphQLPlugin))]

namespace FluentCMS.Plugins.GraphQL;

// GraphQL as a second surface over the services of REST (decision 24): one endpoint, /graphql, whose schema is built from
// the content schema at start. It reads and writes content through IContentService alone.
[FluentCmsPlugin("graphql", SdkRange = "[1.0.0,2.0.0)")]
public sealed class GraphQLPlugin : IPlugin
{
    // A name of its own, so that a host with a HotChocolate schema of its own keeps the default one.
    internal const string SchemaName = "FluentCMS";

    private const string Prefix = "/graphql";

    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        plugin.Services.AddOptions<GraphQLOptions>().BindConfiguration(GraphQLOptions.Section);
        plugin.Services.AddSingleton<IValidateOptions<GraphQLOptions>, GraphQLOptionsValidation>();
        plugin.Services.AddSingleton(services => new ContentModel(services.GetRequiredService<SchemaModel>(), services.GetRequiredService<IFieldTypeRegistry>()));
        plugin.Services.AddSingleton(services => new PersistedQueries(
            services.GetRequiredService<IOptions<GraphQLOptions>>().Value.PersistedQueries,
            services.GetRequiredService<IHostEnvironment>().ContentRootPath));
        plugin.Services.AddHostedService<GraphQLStartup>();

        // HotChocolate's default security is replaced by the plugin's own: the limits come from the options, and
        // introspection is off for the schema and allowed per request, where the caller may.
        plugin.Services
            .AddGraphQLServer(SchemaName, disableDefaultSecurity: true)
            .AddCostAnalyzer()
            .DisableIntrospection()
            .AddMaxExecutionDepthRule(new GraphQLOptions().MaxDepth, skipIntrospectionFields: true, allowRequestOverrides: true)

            // The content service and its database context belong to the request, so no two fields run at once.
            .ModifyOptions(schema => schema.DefaultResolverStrategy = ExecutionStrategy.Serial)
            .ModifyRequestOptions((services, request) =>
            {
                var options = Options(services);
                request.ExecutionTimeout = options.ExecutionTimeout;
                request.IncludeExceptionDetails = false;
                request.PersistedOperations.OnlyAllowPersistedDocuments = options.OnlyPersisted;
                request.PersistedOperations.OperationNotAllowedError = ErrorBuilder.New()
                    .SetMessage("Only persisted queries are allowed: name one by its id.")
                    .SetCode(GraphQLErrorCodes.OnlyPersistedQueries)
                    .Build();
            })
            .ConfigureSchemaServices((application, services) =>
            {
                var options = application.GetRequiredService<IOptions<GraphQLOptions>>().Value;
                services.AddSingleton<Action<CostOptions>>(cost =>
                {
                    cost.MaxFieldCost = options.MaxFieldCost;
                    cost.MaxTypeCost = options.MaxTypeCost;
                });
                services.RemoveAll<IOperationDocumentStorage>();
                services.AddSingleton<IOperationDocumentStorage>(application.GetRequiredService<PersistedQueries>());
            })
            .ConfigureSchema((services, schema) =>
            {
                using var scope = Application(services).CreateScope();
                new ContentSchema(scope.ServiceProvider.GetRequiredService<ContentModel>(), scope.ServiceProvider.GetRequiredService<IContentSurface>().DefaultPageSize).AddTo(schema);
                foreach (var contributor in scope.ServiceProvider.GetServices<IGraphQLSchemaContributor>())
                {
                    contributor.Configure(schema);
                }
            })
            .AddHttpRequestInterceptor<GraphQLRequestInterceptor>()
            .UsePersistedOperationPipeline()
            .UseRequest(GraphQLRequests.WithCodes, key: nameof(GraphQLRequests.WithCodes), before: WellKnownRequestMiddleware.InstrumentationMiddleware);

        plugin
            .AddErrorCode(GraphQLErrorCodes.InvalidOptions, 500, "Invalid GraphQL options")
            .AddErrorCode(GraphQLErrorCodes.SchemaConflict, 500, "GraphQL name conflict")
            .AddErrorCode(GraphQLErrorCodes.InvalidArguments, 400, "Invalid GraphQL arguments")
            .AddErrorCode(GraphQLErrorCodes.IntrospectionNotAllowed, 403, "Introspection not allowed")
            .AddErrorCode(GraphQLErrorCodes.DepthExceeded, 400, "GraphQL query too deep")
            .AddErrorCode(GraphQLErrorCodes.CostExceeded, 400, "GraphQL query too costly")
            .AddErrorCode(GraphQLErrorCodes.OnlyPersistedQueries, 400, "Only persisted queries are allowed")
            .AddErrorCode(GraphQLErrorCodes.PersistedQueryNotFound, 400, "Persisted query not found")
            .AddErrorCode(GraphQLErrorCodes.Timeout, 503, "GraphQL execution timed out");
    }

    // An anonymous caller is the Public role of its site, as on the Delivery API, so the group's sign-in requirement is
    // lifted; what a caller may read or write is the content service's to decide. The endpoint is HTTP alone: no
    // subscriptions, uploads, batches or schema download.
    public void Configure(IPluginAppBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var limit = app.Services.GetRequiredService<IOptions<GraphQLOptions>>().Value.MaxRequestSize;
        var group = app.MapGroup(Prefix).AllowAnonymous().RequireCallerRateLimit();
        group.AddEndpointFilter(async (context, next) =>
        {
            GraphQLRequests.LimitBody(context.HttpContext, limit);
            return await next(context);
        });
        group.MapGraphQLHttp("", SchemaName).WithOptions(server =>
        {
            server.EnableMultipartRequests = false;
            server.EnableSchemaRequests = false;
            server.EnableSchemaFileSupport = false;
            server.Batching = AllowedBatching.None;
        });
    }

    private static GraphQLOptions Options(IServiceProvider services) => Application(services).GetRequiredService<IOptions<GraphQLOptions>>().Value;

    // The schema has services of its own, with the application's behind them.
    private static IServiceProvider Application(IServiceProvider services) =>
        services.GetService<ContentModel>() is not null ? services : services.GetRootServiceProvider();
}
