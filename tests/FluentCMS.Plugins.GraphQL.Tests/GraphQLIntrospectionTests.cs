using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.GraphQL.Tests;

// Who may introspect when OpenIntrospection is left unset: anyone in Development, and elsewhere a signed-in caller that
// holds schema.read in the site of the request. The test hosts run in Development, so the decision is asked directly.
public sealed class GraphQLIntrospectionTests
{
    private static readonly SiteId Site = new(Guid.CreateVersion7());

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Development", false, false, true)]
    [InlineData("Production", false, false, false)]
    [InlineData("Production", true, false, false)]
    [InlineData("Production", true, true, true)]
    [InlineData("Staging", false, true, false)]
    [InlineData("Staging", true, true, true)]
    public async Task OutsideDevelopmentOnlyASignedInCallerWithSchemaReadMayIntrospect(string environment, bool signedIn, bool schemaRead, bool allowed)
    {
        var principal = signedIn ? new Principal(PrincipalKind.ApiKey, Guid.CreateVersion7(), "key") : Principal.Anonymous;
        await using var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new Environment(environment))
            .AddSingleton<IOperationContextAccessor>(new OperationContextAccessor { Current = new OperationContext(principal, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow) })
            .AddSingleton<IPermissionChecker>(new Checker(schemaRead))
            .BuildServiceProvider();

        (await GraphQLRequestInterceptor.MayIntrospectAsync(services, new GraphQLOptions(), Token)).ShouldBe(allowed);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task OpenIntrospectionOverridesTheEnvironment(string environment, bool open)
    {
        await using var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new Environment(environment))
            .AddSingleton<IOperationContextAccessor>(new OperationContextAccessor { Current = new OperationContext(Principal.Anonymous, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow) })
            .AddSingleton<IPermissionChecker>(new Checker(schemaRead: false))
            .BuildServiceProvider();

        (await GraphQLRequestInterceptor.MayIntrospectAsync(services, new GraphQLOptions { OpenIntrospection = open }, Token)).ShouldBe(open);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // Allows schema.read in the site of the request, or nothing.
    private sealed class Checker(bool schemaRead) : IPermissionChecker
    {
        public ValueTask<bool> AllowsAsync(string permission, SiteId? site, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schemaRead && permission == "schema.read" && site == Site);

        public ValueTask RequireAsync(string permission, SiteId? site, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlySet<string>?> ResourcesAsync(string permission, SiteId? site, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> HoldsGrantInAsync(SiteId site, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
