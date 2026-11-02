using System.Reflection;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Localization;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Localization;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Core.DependencyInjection;

public static class FluentCmsServiceCollectionExtensions
{
    public static IServiceCollection AddFluentCms(this IServiceCollection services, Action<FluentCmsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new FluentCmsBuilder(services);
        configure(builder);

        var provider = builder.DataProvider ?? throw new FluentCmsConfigurationException(
            StorageErrorCodes.NoDataProvider,
            "AddFluentCms has no data provider. Add the package for your database and call cms.UseSqlite(...), cms.UsePostgreSql(...) or cms.UseSqlServer(...) inside AddFluentCms.");

        services.AddSingleton(provider);
        services.AddSingleton(provider.Conventions);
        services.AddSingleton(provider.JsonDialect);
        services.AddSingleton(provider.JobClaimDialect);

        // Until content types can be added to the builder, a schema comes from the container, and none means system tables only.
        services.TryAddSingleton(_ => new SchemaBuilder().Build());

        services.TryAddSingleton(serviceProvider => new PermissionCatalogue(
            serviceProvider.GetRequiredService<SchemaModel>(),
            serviceProvider.GetServices<IPermissionProvider>()));

        services.AddSingleton<RoleVersionInterceptor>();

        var contentMigrations = builder.ContentMigrationsAssembly ?? Assembly.GetEntryAssembly() ?? typeof(CmsDbContext).Assembly;
        services.AddDbContext<CmsDbContext>((serviceProvider, options) =>
        {
            ContentMigrations.Configure(options, provider, serviceProvider.GetRequiredService<SchemaModel>(), contentMigrations);
            options.AddInterceptors(serviceProvider.GetRequiredService<RoleVersionInterceptor>());
        });

        services.AddDbContext<SystemDbContext>(options =>
        {
            provider.ConfigureSystemMigrations(options);
            options.UseSystemModel(provider.Conventions);
        });

        services.AddLogging();
        services.AddSingleton<MigrationRunner>();
        services.AddHostedService<MigrationStartupService>();

        services.TryAddSingleton<ContentQueryOptions>();
        services.TryAddSingleton<IOperationContextAccessor, OperationContextAccessor>();
        services.TryAddSingleton<IPrincipalRoles, ClaimPrincipalRoles>();
        services.TryAddSingleton<EffectiveGrantCache>();
        services.AddSingleton(serviceProvider => new RowRuleRegistry(
            serviceProvider.GetRequiredService<SchemaModel>(),
            serviceProvider.GetRequiredService<ContentQueryOptions>(),
            builder.Authorization.Rules));
        services.AddHostedService<RowRuleStartupCheck>();
        services.TryAddScoped<IAuthorizationEvaluator, AuthorizationEvaluator>();
        services.TryAddScoped<IContentQueryAccess, ContentQueryAccess>();
        services.AddScoped<ContentQueryEngine>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ISiteResolver, SiteResolver>();
        services.TryAddScoped<ILocaleResolver, LocaleResolver>();
        services.AddScoped<LocaleRegistry>();
        services.AddScoped<LocaleFallbackReader>();
        services.AddScoped<SiteManager>();

        services.TryAddSingleton<IRichTextRenderer, RichTextRenderer>();
        services.TryAddSingleton<ContentEntryMapper>();
        services.TryAddSingleton<IHookEntryMapper>(serviceProvider => serviceProvider.GetRequiredService<ContentEntryMapper>());
        services.TryAddSingleton<EntryCanonicalizer>();
        services.TryAddSingleton<EntryComposer>();
        services.TryAddSingleton<ResponseRichText>();
        services.AddScoped<HookPipeline>();
        services.AddScoped<ValidationPipeline>();
        services.AddScoped<EntryStore>();
        services.AddScoped<ContentService>();
        services.TryAddScoped<IContentService>(serviceProvider => serviceProvider.GetRequiredService<ContentService>());
        services.TryAddScoped(typeof(IContentService<>), typeof(TypedContentService<>));

        return services;
    }
}
