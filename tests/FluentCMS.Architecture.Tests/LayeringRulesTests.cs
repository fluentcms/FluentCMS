using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Shouldly;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FluentCMS.Architecture.Tests;

// The rules of section 3.4 of the backend technical specification, one test each.
public class LayeringRulesTests
{
    private const string Sdk = "FluentCMS.Sdk";
    private const string PluginAbstractions = "FluentCMS.Plugins.Abstractions";
    private const string PluginPrefix = "FluentCMS.Plugins.";
    private const string Analyzers = "FluentCMS.Analyzers";
    private const string Generators = "FluentCMS.Generators";
    private const string GraphQLPlugin = "FluentCMS.Plugins.GraphQL";

    private const string ApiNamespace = "FluentCMS.Core.Api";
    private const string StorageNamespace = "FluentCMS.Core.Storage";
    private const string AuthorizationNamespace = "FluentCMS.Core.Authorization";
    private const string ContentNamespace = "FluentCMS.Core.Content";
    private const string SitesNamespace = "FluentCMS.Core.Sites";
    private const string ProvidersNamespace = "FluentCMS.Data";
    private const string CliNamespace = "FluentCMS.Cli";
    private const string CliProject = "FluentCMS.Cli";
    private const string ClientProject = "FluentCMS.Client";
    private const string TestingNamespace = "FluentCMS.Testing";
    private const string McpPlugin = "FluentCMS.Plugins.Mcp";
    private const string MultiSitePlugin = "FluentCMS.Plugins.MultiSite";
    private const string WebhooksPlugin = "FluentCMS.Plugins.Webhooks";
    private const string AiAssistPlugin = "FluentCMS.Plugins.AiAssist";
    private const string SearchPlugin = "FluentCMS.Plugins.Search";

    private static readonly string[] McpSiteData = ["McpModel", "McpInstructions", "McpStoredPrompts", "McpInstructionService", "McpPromptService"];

    // The service of the Management endpoints of the MCP clients, which checks mcp.manage.
    private static readonly string[] McpClientServices = ["McpClientService"];

    private static readonly Lazy<ArchUnitNET.Domain.Architecture> core = new(
        () => new ArchLoader().LoadAssemblies(Assembly.Load("FluentCMS.Core")).Build());

    private static readonly Lazy<ArchUnitNET.Domain.Architecture> everything = new(
        () => new ArchLoader().LoadAssemblies([.. ContentTableReaches.SourceAssemblies().Select(Assembly.LoadFrom)]).Build());

    // FluentCMS.Core.Api does not exist until the Delivery API (B-47), so the rule has nothing to check yet and starts checking with the first type there.
    [Fact]
    public void ApiDoesNotReferenceStorageOrCmsDbContext()
    {
        Types().That().ResideInNamespaceOrBelow(ApiNamespace)
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow(StorageNamespace)
            .AndShould().NotDependOnAnyTypesThat().HaveName("CmsDbContext")
            .WithoutRequiringPositiveResults()
            .Check(core.Value);
    }

    [Fact]
    public void PluginsReferenceOnlyTheContractPackagesAmongFluentCmsProjects()
    {
        var plugins = ProjectFile.SourceProjects
            .Where(project => project.Name.StartsWith(PluginPrefix, StringComparison.Ordinal) && project.Name != PluginAbstractions)
            .ToList();

        plugins.ShouldNotBeEmpty();

        var violations = plugins
            .SelectMany(plugin => FluentCmsReferences(plugin)
                .Except(AllowedPluginReferences(plugin, plugins))
                .Select(reference => $"{plugin.Name} references {reference}"))
            .ToList();

        violations.ShouldBeEmpty();
    }

    // Spec 6.4: the reference host, the Blog sample and every first-party plugin build with the analyzers, as analyzers only,
    // and a plugin keeps them out of the dependencies of its package.
    [Fact]
    public void TheHostTheSampleAndThePluginsBuildWithTheAnalyzers()
    {
        var projects = ProjectFile.SourceProjects
            .Where(project => project.Name == "FluentCMS.Host" || (project.Name.StartsWith(PluginPrefix, StringComparison.Ordinal) && project.Name != PluginAbstractions))
            .Append(ProjectFile.Load("samples/Blog/Blog.csproj"))
            .ToList();

        projects.Count.ShouldBeGreaterThan(2);
        projects.Where(project => !project.AnalyzerReferences.Any(reference => reference.Name == Analyzers)).Select(project => project.Name).ShouldBeEmpty();
        projects.Where(project => project.ProjectReferences.Contains(Analyzers)).Select(project => project.Name).ShouldBeEmpty();
        projects.Where(project => project.Name.StartsWith(PluginPrefix, StringComparison.Ordinal))
            .Where(project => project.AnalyzerReferences.Any(reference => reference.Name == Analyzers && !reference.Private))
            .Select(project => project.Name)
            .ShouldBeEmpty();
    }

    // Spec 6.2: the plugin of the fluentcms-plugin template is held to the rule of a first-party plugin: among the FluentCMS
    // packages it depends on the two contract packages only, and it builds with the generator and the analyzers as
    // development dependencies, which keeps them out of its package. Its providers are compiled against only.
    [Fact]
    public void ThePluginTemplateReferencesTheSdkAndThePluginAbstractionsOnly()
    {
        var plugin = ProjectFile.Load("templates/fluentcms-plugin/src/FluentCms-Plugin1/FluentCms-Plugin1.csproj");

        plugin.ProjectReferences.ShouldBeEmpty();
        plugin.PackageReferences.Where(package => !package.DevelopmentOnly).Select(package => package.Name)
            .ShouldBe([Sdk, PluginAbstractions], ignoreOrder: true);
        plugin.PackageReferences.Where(package => package.DevelopmentOnly && package.Name.StartsWith("FluentCMS.", StringComparison.Ordinal))
            .Select(package => package.Name)
            .ShouldBe([Generators, Analyzers], ignoreOrder: true);
    }

    // Spec 6.2: a host from the fluentcms template references the generator and the analyzers as packages of their own,
    // as development dependencies.
    [Fact]
    public void TheHostTemplateBuildsWithTheGeneratorAndTheAnalyzers()
    {
        var host = ProjectFile.Load("templates/fluentcms/FluentCms-App.csproj");

        host.PackageReferences.Where(package => package.DevelopmentOnly).Select(package => package.Name)
            .ShouldBe([Generators, Analyzers], ignoreOrder: true);
    }

    // Spec 2.1: the GraphQL layer carries identity and intent only. It reaches content through IContentService, so it has
    // no context of its own and none of EF Core.
    [Fact]
    public void TheGraphQLPluginNeverTouchesADbContext()
    {
        Types().That().ResideInNamespaceOrBelow(GraphQLPlugin)
            .Should().NotDependOnAnyTypesThat().HaveFullNameContaining("Microsoft.EntityFrameworkCore")
            .AndShould().NotDependOnAnyTypesThat().HaveFullNameContaining("DbContext")
            .Check(everything.Value);

        // The rule finds a plugin that does have one: the webhooks plugin keeps its webhooks in tables of its own.
        Types().That().ResideInNamespaceOrBelow("FluentCMS.Plugins.Webhooks")
            .Should().NotDependOnAnyTypesThat().HaveFullNameContaining("DbContext")
            .HasNoViolations(everything.Value).ShouldBeFalse();
    }

    // What a caller may read or write is the content service's to decide. The one permission the plugin asks about is
    // whether the caller may introspect the schema, and IPermissionChecker answers it for the request interceptor.
    [Fact]
    public void TheGraphQLPluginNeverEvaluatesAPermissionItself()
    {
        Types().That().ResideInNamespaceOrBelow(GraphQLPlugin)
            .And().DoNotHaveFullNameContaining("GraphQLRequestInterceptor")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow("FluentCMS.Authorization")
            .Check(everything.Value);

        Types().That().HaveFullNameContaining("GraphQLRequestInterceptor")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow("FluentCMS.Authorization")
            .HasNoViolations(everything.Value).ShouldBeFalse();
    }

    [Fact]
    public void SdkReferencesNoImplementationPackage()
    {
        var sdk = ProjectFile.SourceProjects.Single(project => project.Name == Sdk);

        var violations = sdk.ProjectReferences
            .Select(reference => $"{Sdk} references project {reference}")
            .Concat(sdk.PackageReferences
                .Where(package => !package.DevelopmentOnly && !IsExtensionsAbstractions(package.Name))
                .Select(package => $"{Sdk} references package {package.Name}"))
            .ToList();

        violations.ShouldBeEmpty();
    }

    // Spec 2.4: the client that generated C# compiles against references no other FluentCMS package, neither in its project
    // nor in its assembly, so an application in another process gets none of the host's types; it needs no package at all.
    [Fact]
    public void TheClientReferencesNoOtherFluentCmsPackage()
    {
        var client = ProjectFile.SourceProjects.Single(project => project.Name == ClientProject);

        client.ProjectReferences.ShouldBeEmpty();
        client.PackageReferences.ShouldBeEmpty();
        Assembly.Load(ClientProject).GetReferencedAssemblies().Select(reference => reference.Name)
            .Where(name => name!.StartsWith("FluentCMS.", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    // Spec 6.1: the tool parses the command line and hands a command to the host as arguments, so Core and everything a
    // host references keeps no dependency on the parser.
    [Fact]
    public void OnlyTheToolReferencesTheCommandLineParser()
    {
        ProjectFile.SourceProjects
            .Where(project => project.PackageReferences.Any(package => package.Name == "System.CommandLine"))
            .Select(project => project.Name)
            .ShouldBe([CliProject]);
    }

    [Fact]
    public void TheMcpPluginReferencesTheContractPackagesAndTheProtocolPackageOnly()
    {
        var mcp = ProjectFile.SourceProjects.Single(project => project.Name == McpPlugin);

        mcp.ProjectReferences.ShouldBe([Sdk, PluginAbstractions], ignoreOrder: true);
        // The providers of its PostgreSQL and SQL Server migration sets are compiled against and not depended on.
        mcp.PackageReferences.Where(package => !package.DevelopmentOnly).Select(package => package.Name).ShouldBe(["ModelContextProtocol.AspNetCore"]);
    }

    // Spec 5.6: the multi-site plugin reaches sites through ISiteAdministration of the SDK, so the two contract packages are
    // all it references, and no type of it names one of core's.
    [Fact]
    public void TheMultiSitePluginReferencesTheSdkAndThePluginAbstractionsOnly()
    {
        var multiSite = ProjectFile.SourceProjects.Single(project => project.Name == MultiSitePlugin);

        multiSite.ProjectReferences.ShouldBe([Sdk, PluginAbstractions], ignoreOrder: true);
        // The providers of its PostgreSQL and SQL Server migration sets are compiled against and not depended on.
        multiSite.PackageReferences.Where(package => !package.DevelopmentOnly).ShouldBeEmpty();
        Types().That().ResideInNamespaceOrBelow(MultiSitePlugin)
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow("FluentCMS.Core")
            .AndShould().NotDependOnAnyTypesThat().HaveName("CmsDbContext")
            .Check(everything.Value);
    }

    // The MCP server is a surface (2.1): it carries the identity and the intent of a call and decides nothing. It holds no
    // database context, content's or a plugin's, and asks no permission; a provider reaches content through the services,
    // which do both. The webhooks plugin, whose service does both, shows that the rule sees what it forbids. What a site
    // keeps for its agents (B-73) is the plugin's own data and not content: the model and the migrations of its two tables,
    // the two readers of them, and the two Management services, which check mcp.manage as the webhooks service checks its
    // permission. The service of the MCP clients (B-71) checks mcp.manage too, and keeps no data: core does. The rest of
    // the plugin is held to the rule, and none of it reaches the content context.
    [Fact]
    public void TheMcpPluginReachesContentThroughTheServicesAndEvaluatesNoPermission()
    {
        SurfaceRule(McpPlugin, [.. McpSiteData, .. McpClientServices]).Check(everything.Value);
        Types().That().ResideInNamespaceOrBelow(McpPlugin).Should().NotDependOnAnyTypesThat().HaveName("CmsDbContext").Check(everything.Value);

        // Each exception is needed: without the clients' service, or without the site's data, the rule is broken.
        Should.Throw<Exception>(() => SurfaceRule(McpPlugin, McpSiteData).Check(everything.Value));
        Should.Throw<Exception>(() => SurfaceRule(McpPlugin, McpClientServices).Check(everything.Value));

        Should.Throw<Exception>(() => SurfaceRule(McpPlugin).Check(everything.Value));
        Should.Throw<Exception>(() => SurfaceRule(WebhooksPlugin).Check(everything.Value));
    }

    [Fact]
    public void TheAiAssistPluginReferencesTheContractPackagesAndItsProviderPackagesOnly()
    {
        var ai = ProjectFile.SourceProjects.Single(project => project.Name == AiAssistPlugin);

        ai.ProjectReferences.ShouldBe([Sdk, PluginAbstractions], ignoreOrder: true);
        // The providers of its PostgreSQL and SQL Server migration sets are compiled against and not depended on.
        ai.PackageReferences.Where(package => !package.DevelopmentOnly).Select(package => package.Name)
            .ShouldBe(["Microsoft.Extensions.AI", "Microsoft.Extensions.AI.OpenAI"], ignoreOrder: true);
    }

    // Spec 5.5: the AI assist plugin reads content and media through their services, as the caller, and writes nothing to
    // content. The one thing it keeps is its usage log: the model and the migrations of that table, the service that writes
    // and pages it and the job that prunes it. The rest of the plugin holds no database context, and of the content and
    // media services it calls the reads alone. The MCP plugin, whose tools write through the same services, shows that the
    // scan of the calls sees a write.
    [Fact]
    public void TheAiAssistPluginReadsThroughTheServicesAndWritesNothingToContent()
    {
        Types().That().ResideInNamespaceOrBelow(AiAssistPlugin)
            .And().DoNotResideInNamespaceOrBelow(AiAssistPlugin + ".Migrations")
            .And().DoNotHaveName("AiUsageModel")
            .And().DoNotHaveName("AiUsageService")
            .And().DoNotHaveName("AiUsageRetentionJob")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow("Microsoft.EntityFrameworkCore")
            .AndShould().NotDependOnAnyTypesThat().HaveNameStartingWith("PluginDbContext")
            .Check(everything.Value);
        Types().That().ResideInNamespaceOrBelow(AiAssistPlugin).Should().NotDependOnAnyTypesThat().HaveName("CmsDbContext").Check(everything.Value);
        Types().That().ResideInNamespaceOrBelow(AiAssistPlugin)
            .Should().NotDependOnAnyTypesThat().HaveNameStartingWith("PluginDbContext")
            .HasNoViolations(everything.Value).ShouldBeFalse();

        ServiceCalls.In(AiAssistPlugin).ShouldBe(
            ["FluentCMS.Content.IContentService.GetAsync", "FluentCMS.Media.IMediaService.GetAsync", "FluentCMS.Media.IMediaService.OpenImageAsync"],
            ignoreOrder: true);
        ServiceCalls.In(McpPlugin).ShouldContain("FluentCMS.Content.IContentService.CreateAsync");
    }

    // Spec 5.7: the search plugin fills the ISearchIndex slot with the two contract packages alone, and core does not change
    // for it. It reads entries through the content service, and its own index alone holds what it writes; only its store
    // reaches the database with SQL, which the scan of the calls holds to that one type.
    [Fact]
    public void TheSearchPluginReferencesTheSdkAndThePluginAbstractionsOnly()
    {
        var search = ProjectFile.SourceProjects.Single(project => project.Name == SearchPlugin);

        search.ProjectReferences.ShouldBe([Sdk, PluginAbstractions], ignoreOrder: true);
        // The providers of its PostgreSQL and SQL Server migration sets are compiled against and not depended on.
        search.PackageReferences.Where(package => !package.DevelopmentOnly).ShouldBeEmpty();
        Types().That().ResideInNamespaceOrBelow(SearchPlugin)
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow("FluentCMS.Core")
            .AndShould().NotDependOnAnyTypesThat().HaveName("CmsDbContext")
            .Check(everything.Value);
        ServiceCalls.In(SearchPlugin).ShouldBe(
            ["FluentCMS.Content.IContentService.GetAsync", "FluentCMS.Content.IContentService.QueryAsync"],
            ignoreOrder: true);
    }

    // The contracts of ADR 0003 that need EF Core or ASP.NET Core, which the framework reference brings.
    [Fact]
    public void PluginAbstractionsReferenceOnlyTheSdkAndEfCore()
    {
        var abstractions = ProjectFile.SourceProjects.Single(project => project.Name == PluginAbstractions);

        var violations = abstractions.ProjectReferences
            .Where(reference => reference != Sdk)
            .Select(reference => $"{PluginAbstractions} references project {reference}")
            .Concat(abstractions.PackageReferences
                .Where(package => !package.DevelopmentOnly && !package.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) && !IsExtensionsAbstractions(package.Name))
                .Select(package => $"{PluginAbstractions} references package {package.Name}"))
            .ToList();

        violations.ShouldBeEmpty();
        abstractions.ProjectReferences.ShouldContain(Sdk);
    }

    [Fact]
    public void OnlyAuthorizationReadsRolePermissionEntities()
    {
        // The Storage namespace declares the entities and maps them, which is not reading them.
        Types().That().DoNotResideInNamespaceOrBelow(AuthorizationNamespace)
            .And().DoNotResideInNamespaceOrBelow(StorageNamespace)
            .Should().NotDependOnAnyTypesThat().HaveNameStartingWith("RoleGrant")
            .Check(core.Value);
    }

    // Issue #2693. CmsDbContext also holds the system tables, so these may reach it for their own tables only, which the
    // next rules check: the evaluator, UserPrincipalRoles and ApiKeyPrincipals for roles and keys, Sites and LocaleRegistry
    // for cms_site and cms_site_locale, the extensions that register it, and the provider packages and the tool, which are the storage layer's other half.
    // RoleService writes roles through it, whose interceptor raises their permissions version, and the test host writes them the same way.
    [Fact]
    public void OnlyTheServiceAndTheStorageLayerTouchCmsDbContext()
    {
        Types().That().DoNotResideInNamespaceOrBelow(ContentNamespace)
            .And().DoNotResideInNamespaceOrBelow(StorageNamespace)
            .And().DoNotResideInNamespaceOrBelow(ProvidersNamespace)
            .And().DoNotResideInNamespaceOrBelow(CliNamespace)
            .And().DoNotResideInNamespaceOrBelow(SitesNamespace)
            .And().DoNotResideInNamespaceOrBelow(TestingNamespace)
            .And().DoNotHaveName("AuthorizationEvaluator")
            .And().DoNotHaveName("RoleService")
            .And().DoNotHaveName("UserPrincipalRoles")
            .And().DoNotHaveName("ApiKeyPrincipals")
            .And().DoNotHaveName("LocaleRegistry")
            .And().DoNotHaveName("FluentCmsServiceCollectionExtensions")
            .Should().NotDependOnAnyTypesThat().HaveName("CmsDbContext")
            .Check(everything.Value);
    }

    // The evaluator asks whether an entry is in a site, and SiteManager whether a site has entries; neither reads content.
    [Fact]
    public void OnlyTheServiceAndTheStorageLayerTouchTheContentSystemTables()
    {
        Types().That().DoNotResideInNamespaceOrBelow(ContentNamespace)
            .And().DoNotResideInNamespaceOrBelow(StorageNamespace)
            .And().DoNotResideInNamespaceOrBelow(ProvidersNamespace)
            .And().DoNotHaveName("AuthorizationEvaluator")
            .And().DoNotHaveName("SiteManager")
            .Should().NotDependOnAnyTypesThat().HaveName("Document")
            .AndShould().NotDependOnAnyTypesThat().HaveName("Reference")
            .AndShould().NotDependOnAnyTypesThat().HaveName("ContentVersion")
            .Check(everything.Value);
    }

    // Inside Content, the store and the query engine are what the service and the evaluator go through, not a way around them.
    [Fact]
    public void NothingOutsideContentUsesTheEntryStoreOrWritesAroundTheService()
    {
        Types().That().DoNotResideInNamespaceOrBelow(ContentNamespace)
            .And().DoNotHaveName("FluentCmsServiceCollectionExtensions")
            .Should().NotDependOnAnyTypesThat().HaveName("EntryStore")
            .Check(everything.Value);
    }

    // A content table has no class to depend on, so these look at the calls: its rows by name, or SQL.
    [Fact]
    public void OnlyTheServiceAndTheStorageLayerReachContentTablesByNameOrBySql()
    {
        var violations = ContentTableReaches.InEveryAssembly().ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void EveryProjectUnderSrcIsScanned()
    {
        var scanned = ContentTableReaches.SourceAssemblies().Select(Path.GetFileNameWithoutExtension).ToList();

        scanned.ShouldBe(ProjectFile.SourceProjects.Select(project => project.Name), ignoreOrder: true);
    }

    [Fact]
    public void TheCallScanFindsEveryWayToAContentTable()
    {
        var found = ContentTableReaches.In(typeof(CarelessCode).Assembly.Location).Where(violation => violation.Contains(nameof(CarelessCode), StringComparison.Ordinal)).ToList();

        found.ShouldContain(violation => violation.Contains("Set", StringComparison.Ordinal));
        found.ShouldContain(violation => violation.Contains("Rows", StringComparison.Ordinal));
        found.ShouldContain(violation => violation.Contains("get_Database", StringComparison.Ordinal));
        found.ShouldContain(violation => violation.Contains("ExecuteSqlRaw", StringComparison.Ordinal));
        found.ShouldContain(violation => violation.Contains("FromSqlRaw", StringComparison.Ordinal));
    }

    // The types named keep data of the plugin's own, as its migrations do, or check the plugin's permission.
    private static ArchUnitNET.Fluent.Syntax.Elements.Types.TypesShouldConjunction SurfaceRule(string plugin, params string[] ownData)
    {
        var types = Types().That().ResideInNamespaceOrBelow(plugin);
        if (ownData.Length > 0)
        {
            types = types.And().DoNotResideInNamespaceOrBelow(plugin + ".Migrations");
        }

        foreach (var name in ownData)
        {
            types = types.And().DoNotHaveName(name);
        }

        return types
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow("Microsoft.EntityFrameworkCore")
            .AndShould().NotDependOnAnyTypesThat().HaveNameStartingWith("PluginDbContext")
            .AndShould().NotDependOnAnyTypesThat().HaveName("IPermissionChecker");
    }

    private static IEnumerable<string> FluentCmsReferences(ProjectFile project) =>
        project.ProjectReferences
            .Concat(project.PackageReferences.Select(package => package.Name))
            .Where(name => name.StartsWith("FluentCMS.", StringComparison.Ordinal));

    // A provider plugin such as Media.S3 builds on the plugin it provides for, per the package map (2.4). A plugin that adds
    // types to the GraphQL schema implements IGraphQLSchemaContributor, which is in the GraphQL plugin, since the SDK takes
    // no HotChocolate dependency.
    private static IEnumerable<string> AllowedPluginReferences(ProjectFile plugin, IReadOnlyList<ProjectFile> plugins)
    {
        yield return Sdk;
        yield return PluginAbstractions;
        if (plugin.Name != GraphQLPlugin)
        {
            yield return GraphQLPlugin;
        }

        var parent = plugin.Name[..plugin.Name.LastIndexOf('.')];
        if (plugins.Any(other => other.Name == parent))
        {
            yield return parent;
        }
    }

    private static bool IsExtensionsAbstractions(string packageName) =>
        packageName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
        && packageName.EndsWith(".Abstractions", StringComparison.Ordinal);
}
