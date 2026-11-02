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

    private static readonly Lazy<ArchUnitNET.Domain.Architecture> core = new(
        () => new ArchLoader().LoadAssemblies(Assembly.Load("FluentCMS.Core")).Build());

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
    // next rule checks: the evaluator for roles, Sites and LocaleRegistry for cms_site and cms_site_locale, and
    // the extensions that register it.
    [Fact]
    public void OnlyTheServiceAndTheStorageLayerTouchCmsDbContext()
    {
        Types().That().DoNotResideInNamespaceOrBelow(ContentNamespace)
            .And().DoNotResideInNamespaceOrBelow(StorageNamespace)
            .And().DoNotResideInNamespaceOrBelow(SitesNamespace)
            .And().DoNotHaveName("AuthorizationEvaluator")
            .And().DoNotHaveName("LocaleRegistry")
            .And().DoNotHaveName("FluentCmsServiceCollectionExtensions")
            .Should().NotDependOnAnyTypesThat().HaveName("CmsDbContext")
            .Check(core.Value);
    }

    // The evaluator asks whether an entry is in a site, and SiteManager whether a site has entries; neither reads content.
    // Nothing outside Content and Storage reads a content table: Rows is the only way to one, and it is checked below.
    [Fact]
    public void OnlyTheServiceAndTheStorageLayerTouchTheContentSystemTables()
    {
        Types().That().DoNotResideInNamespaceOrBelow(ContentNamespace)
            .And().DoNotResideInNamespaceOrBelow(StorageNamespace)
            .And().DoNotHaveName("AuthorizationEvaluator")
            .And().DoNotHaveName("SiteManager")
            .Should().NotDependOnAnyTypesThat().HaveName("Document")
            .AndShould().NotDependOnAnyTypesThat().HaveName("Reference")
            .AndShould().NotDependOnAnyTypesThat().HaveName("ContentVersion")
            .Check(core.Value);
    }

    [Fact]
    public void OnlyTheServiceAndTheStorageLayerReadContentRows()
    {
        var rows = MethodMembers().That().AreDeclaredIn(typeof(FluentCMS.Core.Storage.CmsDbContext)).And().HaveName("Rows(System.String)");

        Types().That().DoNotResideInNamespaceOrBelow(ContentNamespace)
            .And().DoNotResideInNamespaceOrBelow(StorageNamespace)
            .Should().NotCallAny(rows)
            .Check(core.Value);
    }

    // Inside Content, the store and the query engine are what the service and the evaluator go through, not a way around them.
    [Fact]
    public void NothingOutsideContentUsesTheEntryStoreOrWritesAroundTheService()
    {
        Types().That().DoNotResideInNamespaceOrBelow(ContentNamespace)
            .And().DoNotHaveName("FluentCmsServiceCollectionExtensions")
            .Should().NotDependOnAnyTypesThat().HaveName("EntryStore")
            .Check(core.Value);
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
