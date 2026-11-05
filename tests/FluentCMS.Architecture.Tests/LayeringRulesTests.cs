using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Shouldly;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FluentCMS.Architecture.Tests;

// The four rules of section 3.4 of the backend technical specification, one test each.
public class LayeringRulesTests
{
    private const string Sdk = "FluentCMS.Sdk";
    private const string PluginPrefix = "FluentCMS.Plugins.";

    private const string ApiNamespace = "FluentCMS.Core.Api";
    private const string StorageNamespace = "FluentCMS.Core.Storage";
    private const string AuthorizationNamespace = "FluentCMS.Core.Authorization";
    private const string ContentNamespace = "FluentCMS.Core.Content";
    private const string SitesNamespace = "FluentCMS.Core.Sites";
    private const string ProvidersNamespace = "FluentCMS.Data";
    private const string CliNamespace = "FluentCMS.Cli";
    private const string TestingNamespace = "FluentCMS.Testing";

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
    public void PluginsReferenceOnlyTheSdkAmongFluentCmsProjects()
    {
        var plugins = ProjectFile.SourceProjects
            .Where(project => project.Name.StartsWith(PluginPrefix, StringComparison.Ordinal))
            .ToList();

        plugins.ShouldNotBeEmpty();

        var violations = plugins
            .SelectMany(plugin => FluentCmsReferences(plugin)
                .Except(AllowedPluginReferences(plugin, plugins))
                .Select(reference => $"{plugin.Name} references {reference}"))
            .ToList();

        violations.ShouldBeEmpty();
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
    // for cms_site and cms_site_locale, the extensions that register it, and the provider packages and the tool, which are the storage layer's other half. The test host
    // looks roles up and writes custom ones the way role management will.
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

    private static IEnumerable<string> FluentCmsReferences(ProjectFile project) =>
        project.ProjectReferences
            .Concat(project.PackageReferences.Select(package => package.Name))
            .Where(name => name.StartsWith("FluentCMS.", StringComparison.Ordinal));

    // A provider plugin such as Media.S3 builds on the plugin it provides for, per the package map (2.4).
    private static IEnumerable<string> AllowedPluginReferences(ProjectFile plugin, IReadOnlyList<ProjectFile> plugins)
    {
        yield return Sdk;

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
