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

    [Fact(Skip = "The role permission entities and FluentCMS.Core.Authorization do not exist yet. Enable with the permission catalogue and roles (#2700) and confirm the entity names then.")]
    public void OnlyAuthorizationReadsRolePermissionEntities()
    {
        // The Storage namespace declares the entities and maps them, which is not reading them.
        Types().That().DoNotResideInNamespaceOrBelow(AuthorizationNamespace)
            .And().DoNotResideInNamespaceOrBelow(StorageNamespace)
            .Should().NotDependOnAnyTypesThat().HaveNameStartingWith("RolePermission")
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
