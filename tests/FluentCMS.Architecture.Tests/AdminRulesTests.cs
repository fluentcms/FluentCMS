using System.Reflection;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Shouldly;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FluentCMS.Architecture.Tests;

// The rules of section 3.3 of the admin technical specification. Each runs on the repository and on a project or a type that
// breaks it, so that a rule which no longer sees what it forbids fails too.
public class AdminRulesTests
{
    private const string AdminFolder = "admin/src/";
    private const string HalvesFolder = "admin/src/plugins/";
    private const string Admin = "FluentCMS.Admin";
    private const string AdminAbstractions = "FluentCMS.Admin.Abstractions";
    private const string AdminComponents = "FluentCMS.Admin.Components";
    private const string AdminRichText = "FluentCMS.Admin.RichText";
    private const string AdminApp = "FluentCMS.Admin.App";
    private const string AdminHosting = "FluentCMS.Admin.Hosting";
    private const string Core = "FluentCMS.Core";
    private const string Client = "FluentCMS.Client";

    // Spec 3.1: the types that may use JavaScript interop. The component library's script is loaded by the library's own types,
    // so no type of FluentCMS.Admin.Components is listed. The rich text bundle and the clipboard, file drop and BroadcastChannel
    // modules join the list by name, in FluentCMS.Admin or FluentCMS.Admin.RichText, with the issues that write them.
    private static readonly string[] InteropTypes = ["FluentCMS.Admin.Interop.DocumentInterop"];

    private static readonly string[] InteropNamespaces = ["Microsoft.JSInterop", "System.Runtime.InteropServices.JavaScript"];

    private static readonly Lazy<ArchUnitNET.Domain.Architecture> browser = new(
        () => new ArchLoader().LoadAssemblies([.. BrowserAssemblies().Select(Assembly.LoadFrom)]).Build());

    private static readonly Lazy<ArchUnitNET.Domain.Architecture> careless = new(
        () => new ArchLoader().LoadAssemblies(typeof(CarelessInterop).Assembly).Build());

    // Decision 4: what runs in the browser references no server package, through another project either. FluentCMS.Client,
    // which references nothing, may be reused (AQ6).
    [Fact]
    public void NoAdminAssemblyThatRunsInTheBrowserReferencesAServerPackage()
    {
        BrowserReferencesToServerPackages(ProjectFile.RepositoryProjects).ShouldBeEmpty();

        var careless = ProjectFile.FromXml(AdminFolder + "FluentCMS.Admin.Careless/FluentCMS.Admin.Careless.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../../../src/FluentCMS.Sdk/FluentCMS.Sdk.csproj" />
                <ProjectReference Include="../FluentCMS.Admin.Hosting/FluentCMS.Admin.Hosting.csproj" />
                <PackageReference Include="FluentCMS.Data.Sqlite" />
              </ItemGroup>
            </Project>
            """);

        BrowserReferencesToServerPackages(With(careless)).ShouldBe(
            [
                "FluentCMS.Admin.Careless references FluentCMS.Sdk",
                "FluentCMS.Admin.Careless references FluentCMS.Admin.Hosting",
                "FluentCMS.Admin.Careless references FluentCMS.Core",
                "FluentCMS.Admin.Careless references FluentCMS.Plugins.Abstractions",
                "FluentCMS.Admin.Careless references FluentCMS.Data.Sqlite",
            ],
            ignoreOrder: true);
    }

    // Spec 2.3: a half builds on the contract and the wrapped set alone, so swapping the component library touches
    // FluentCMS.Admin.Components only. The library is what that project references.
    [Fact]
    public void AnAdminHalfReferencesTheAdminAbstractionsAndTheWrappedComponentsOnly()
    {
        HalfViolations(ProjectFile.RepositoryProjects).ShouldBeEmpty();

        var careless = ProjectFile.FromXml(HalvesFolder + "FluentCMS.Plugins.Reviews.Admin/FluentCMS.Plugins.Reviews.Admin.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../../FluentCMS.Admin.Abstractions/FluentCMS.Admin.Abstractions.csproj" />
                <ProjectReference Include="../../FluentCMS.Admin.Components/FluentCMS.Admin.Components.csproj" />
                <ProjectReference Include="../../FluentCMS.Admin/FluentCMS.Admin.csproj" />
                <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" />
              </ItemGroup>
            </Project>
            """);

        HalfViolations(With(careless)).ShouldBe(
            [
                "FluentCMS.Plugins.Reviews.Admin references FluentCMS.Admin",
                "FluentCMS.Plugins.Reviews.Admin references the component library's Microsoft.FluentUI.AspNetCore.Components",
            ],
            ignoreOrder: true);
    }

    // A half is found by its folder, by its name and by what it references, so that one outside admin/src/plugins/, in a sample
    // or a template, is held to the rules too.
    [Fact]
    public void AnAdminHalfIsFoundByItsFolderItsNameOrItsReferences()
    {
        var byFolder = ProjectFile.FromXml(HalvesFolder + "Reviews/Reviews.csproj", "<Project />");
        var byName = ProjectFile.FromXml("samples/Reviews.Admin/Reviews.Admin.csproj", "<Project />");
        var byReference = ProjectFile.FromXml("samples/Reviews.Pages/Reviews.Pages.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <PackageReference Include="FluentCMS.Admin.Components" />
              </ItemGroup>
            </Project>
            """);
        var server = ProjectFile.FromXml("samples/Reviews/Reviews.csproj", "<Project />");

        Halves([.. ProjectFile.RepositoryProjects, byFolder, byName, byReference, server]).Select(half => half.Name)
            .ShouldBe(["Reviews", "Reviews.Admin", "Reviews.Pages"], ignoreOrder: true);
    }

    // Spec 2.8: the contract depends on Microsoft.AspNetCore.Components.Web and the Microsoft.Extensions.* abstractions only.
    [Fact]
    public void TheAdminAbstractionsReferenceNoImplementationPackage()
    {
        AbstractionsViolations(ProjectFile.RepositoryProjects).ShouldBeEmpty();

        var careless = ProjectFile.FromXml(AdminFolder + "FluentCMS.Admin.Abstractions/FluentCMS.Admin.Abstractions.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" PrivateAssets="all" />
                <PackageReference Include="Microsoft.AspNetCore.Components.Web" />
                <PackageReference Include="Microsoft.Extensions.Http" />
                <ProjectReference Include="../FluentCMS.Admin.Components/FluentCMS.Admin.Components.csproj" />
              </ItemGroup>
            </Project>
            """);

        AbstractionsViolations(With(careless)).ShouldBe(
            [
                "FluentCMS.Admin.Abstractions references project FluentCMS.Admin.Components",
                "FluentCMS.Admin.Abstractions references package Microsoft.Extensions.Http",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void TheHostingPackageIsTheOnlyAdminPackageThatReferencesCore()
    {
        AdminProjectsReachingCore(ProjectFile.RepositoryProjects).ShouldBe([AdminHosting]);

        // Every admin project builds on the contract, so a reference from it reaches them all.
        var careless = ProjectFile.FromXml(AdminFolder + "FluentCMS.Admin.Abstractions/FluentCMS.Admin.Abstractions.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../../../src/FluentCMS.Core/FluentCMS.Core.csproj" />
              </ItemGroup>
            </Project>
            """);

        AdminProjectsReachingCore(With(careless)).ShouldBe([AdminHosting, Admin, AdminAbstractions, AdminApp, AdminComponents, AdminRichText], ignoreOrder: true);

        // That no assembly of the browser references the hosting package is checked by the first rule, which does not count
        // the hosting package as one.
        BrowserProjects(ProjectFile.RepositoryProjects).Select(project => project.Name).ShouldNotContain(AdminHosting);
    }

    // Spec 3.1: a type injects or takes IJSRuntime, an IJSObjectReference or the in-process runtime only if it is on the list. A
    // component's @inject compiles to an [Inject] property, which the scan of the assembly sees.
    [Fact]
    public void OnlyTheAllowListedTypesUseJavaScriptInterop()
    {
        InteropRule(InteropTypes).Check(browser.Value);

        // Without the list the rule finds the listed type, and only it: its nested types, such as its async methods, are its own.
        var listed = Violators(InteropRule([]), browser.Value);
        listed.ShouldContain(InteropTypes[0]);
        listed.ShouldAllBe(type => InteropTypes.Any(name => type == name || type.StartsWith(name + "+", StringComparison.Ordinal)));

        Violators(InteropRule(InteropTypes), careless.Value).ShouldBe(
            [typeof(CarelessInterop).FullName!, typeof(CarelessComponent).FullName!, typeof(CarelessModule).FullName!],
            ignoreOrder: true);
    }

    [Fact]
    public void EachAllowListedInteropTypeIsInTheShellOrTheRichTextEditor()
    {
        foreach (var name in InteropTypes)
        {
            var type = browser.Value.Types.SingleOrDefault(type => type.FullName == name);
            type.ShouldNotBeNull($"{name} is on the interop list but is not a type of the admin.");
            type.Assembly.Name.Split(',')[0].ShouldBeOneOf(Admin, AdminRichText);
        }
    }

    // An admin project or half under admin/src/ that the test project does not reference would escape the interop rule. A half
    // of a template or a sample, built from packages, is held to the project rules only.
    [Fact]
    public void EveryAdminAssemblyThatRunsInTheBrowserIsScanned()
    {
        BrowserProjects(ProjectFile.RepositoryProjects)
            .Where(project => project.Location.StartsWith(AdminFolder, StringComparison.Ordinal))
            .Where(project => !File.Exists(Path.Combine(AppContext.BaseDirectory, project.Name + ".dll")))
            .Select(project => project.Name)
            .ShouldBeEmpty();
    }

    private static IEnumerable<string> BrowserAssemblies() =>
        BrowserProjects(ProjectFile.RepositoryProjects).Select(project => Path.Combine(AppContext.BaseDirectory, project.Name + ".dll")).Where(File.Exists);

    private static ArchUnitNET.Fluent.Syntax.Elements.Types.TypesShouldConjunction InteropRule(string[] allowed)
    {
        var rule = Types().That().FollowCustomPredicate(
                type => !allowed.Any(name => type.FullName == name || type.FullName.StartsWith(name + "+", StringComparison.Ordinal)),
                "are not on the interop list of spec 3.1")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow(InteropNamespaces[0]);

        foreach (var name in InteropNamespaces.Skip(1))
        {
            rule = rule.AndShould().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow(name);
        }

        return rule;
    }

    private static List<string> Violators(ArchUnitNET.Fluent.Syntax.Elements.Types.TypesShouldConjunction rule, ArchUnitNET.Domain.Architecture architecture) =>
        [.. rule.Evaluate(architecture).Where(result => !result.Passed).Select(result => result.EvaluatedObjectIdentifier.Identifier).Distinct()];

    // Every project under admin/src/ but the hosting package, which runs in the ASP.NET Core host, and every half found elsewhere.
    private static List<ProjectFile> BrowserProjects(IReadOnlyList<ProjectFile> projects) =>
        [.. projects.Where(project => project.Location.StartsWith(AdminFolder, StringComparison.Ordinal) && project.Name != AdminHosting)
            .Union(Halves(projects))];

    private static List<ProjectFile> Halves(IReadOnlyList<ProjectFile> projects) =>
        [.. projects.Where(project => project.Location.StartsWith(HalvesFolder, StringComparison.Ordinal)
            || (!IsAdminPackage(project) && (project.Name.EndsWith(".Admin", StringComparison.Ordinal)
                || DirectReferences(project).Any(reference => reference is AdminAbstractions or AdminComponents))))];

    // The projects of admin/src/ itself, which are not halves.
    private static bool IsAdminPackage(ProjectFile project) =>
        project.Location.StartsWith(AdminFolder, StringComparison.Ordinal)
        && !project.Location.StartsWith(HalvesFolder, StringComparison.Ordinal);

    private static List<string> BrowserReferencesToServerPackages(IReadOnlyList<ProjectFile> projects)
    {
        var browserProjects = BrowserProjects(projects);
        var browserNames = browserProjects.Select(project => project.Name).ToHashSet(StringComparer.Ordinal);

        return [.. browserProjects.SelectMany(project => Reachable(project, projects)
            .Where(reference => reference.StartsWith("FluentCMS.", StringComparison.Ordinal) && !browserNames.Contains(reference) && reference != Client)
            .Select(reference => $"{project.Name} references {reference}"))];
    }

    private static List<string> HalfViolations(IReadOnlyList<ProjectFile> projects)
    {
        var library = projects.Single(project => project.Name == AdminComponents).PackageReferences
            .Where(package => !package.DevelopmentOnly && !package.Name.StartsWith("FluentCMS.", StringComparison.Ordinal))
            .Select(package => package.Name)
            .ToList();

        library.ShouldNotBeEmpty();

        return [.. Halves(projects).SelectMany(half => DirectReferences(half)
            .Where(reference => reference.StartsWith("FluentCMS.", StringComparison.Ordinal) && reference is not (AdminAbstractions or AdminComponents))
            .Select(reference => $"{half.Name} references {reference}")
            .Concat(DirectReferences(half).Intersect(library).Select(reference => $"{half.Name} references the component library's {reference}")))];
    }

    private static List<string> AbstractionsViolations(IReadOnlyList<ProjectFile> projects)
    {
        var abstractions = projects.Single(project => project.Name == AdminAbstractions);

        return [.. abstractions.ProjectReferences
            .Select(reference => $"{AdminAbstractions} references project {reference}")
            .Concat(abstractions.PackageReferences
                .Where(package => !package.DevelopmentOnly && package.Name != "Microsoft.AspNetCore.Components.Web" && !IsExtensionsAbstractions(package.Name))
                .Select(package => $"{AdminAbstractions} references package {package.Name}"))];
    }

    private static List<string> AdminProjectsReachingCore(IReadOnlyList<ProjectFile> projects)
    {
        var halves = Halves(projects);

        return [.. projects.Where(project => IsAdminPackage(project) || halves.Contains(project))
            .Where(project => Reachable(project, projects).Contains(Core))
            .Select(project => project.Name)];
    }

    private static IEnumerable<string> DirectReferences(ProjectFile project) =>
        project.ProjectReferences.Concat(project.PackageReferences.Where(package => !package.DevelopmentOnly).Select(package => package.Name));

    // What a project references, and what the projects of the repository it references do in turn; a package of the repository
    // is its project.
    private static HashSet<string> Reachable(ProjectFile project, IReadOnlyList<ProjectFile> projects)
    {
        var byName = projects.ToDictionary(other => other.Name, StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(DirectReferences(project));

        while (pending.TryPop(out var name))
        {
            if (reached.Add(name) && byName.TryGetValue(name, out var referenced))
            {
                foreach (var next in DirectReferences(referenced))
                {
                    pending.Push(next);
                }
            }
        }

        return reached;
    }

    private static List<ProjectFile> With(ProjectFile replacement) =>
        [.. ProjectFile.RepositoryProjects.Where(project => project.Name != replacement.Name), replacement];

    private static bool IsExtensionsAbstractions(string packageName) =>
        packageName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
        && packageName.EndsWith(".Abstractions", StringComparison.Ordinal);
}
