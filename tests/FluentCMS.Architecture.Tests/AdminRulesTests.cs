using System.Reflection;
using ArchUnitNET.Fluent;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
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

    // The repository's own half, which every list of halves holds.
    private const string MediaHalf = HalvesFolder + "FluentCMS.Plugins.Media.Admin/FluentCMS.Plugins.Media.Admin.csproj";
    private const string AdminHosting = "FluentCMS.Admin.Hosting";
    private const string Core = "FluentCMS.Core";
    private const string Client = "FluentCMS.Client";
    private const string Library = "Microsoft.FluentUI.AspNetCore.Components";
    private const string LibraryNamespace = "Microsoft.FluentUI";
    private const string TemplateHalf = "templates/fluentcms-plugin/src/FluentCms-Plugin1.Admin/FluentCms-Plugin1.Admin.csproj";

    // The packages of admin/src/ itself; any other project there, or elsewhere, may be a half.
    private static readonly string[] AdminPackages = [Admin, AdminAbstractions, AdminComponents, AdminRichText, AdminApp, AdminHosting];

    // A development-only FluentCMS package is left out of the rules only when it is one of these, which run in the compiler.
    private static readonly string[] CompilerPackages = ["FluentCMS.Analyzers", "FluentCMS.Generators"];

    // Spec 3.1: the types that may use JavaScript interop, the document module, the tab's sessionStorage of the sign-in,
    // localStorage for the theme's one key and for the column names of the lists, and the drop and paste of the media
    // library, in the media half (A-21). The component library's script is loaded by the library's own types, so no type of
    // FluentCMS.Admin.Components is listed. The rich text bundle and the clipboard and BroadcastChannel modules join the list
    // by name, in FluentCMS.Admin or FluentCMS.Admin.RichText, with the issues that write them.
    private static readonly string[] InteropTypes =
    [
        "FluentCMS.Admin.Interop.ColumnStorageInterop",
        "FluentCMS.Admin.Interop.DocumentInterop",
        "FluentCMS.Admin.Interop.SessionStorageInterop",
        "FluentCMS.Admin.Interop.TabChannelInterop",
        "FluentCMS.Admin.Interop.ThemeStorageInterop",
        "FluentCMS.Plugins.Media.Admin.MediaDropZone",
    ];

    private const string MediaHalfAssembly = "FluentCMS.Plugins.Media.Admin";

    private static readonly string[] InteropNamespaces = ["Microsoft.JSInterop", "System.Runtime.InteropServices.JavaScript"];

    private static readonly Lazy<Assembly[]> browserAssemblies = new(() => [.. BrowserAssemblies().Select(Assembly.LoadFrom)]);

    private static readonly Lazy<ArchUnitNET.Domain.Architecture> browser = new(
        () => new ArchLoader().LoadAssemblies(browserAssemblies.Value).Build());

    private static readonly Lazy<ArchUnitNET.Domain.Architecture> careless = new(
        () => new ArchLoader().LoadAssemblies(typeof(CarelessInterop).Assembly).Build());

    // Decision 4: what runs in the browser references no server package, through another project either. FluentCMS.Client,
    // which references nothing, may be reused (AQ5).
    [Fact]
    public void NoAdminAssemblyThatRunsInTheBrowserReferencesAServerPackage()
    {
        BrowserReferencesToServerPackages(ProjectFile.RepositoryProjects).ShouldBeEmpty();

        var careless = ProjectFile.FromXml(AdminFolder + "FluentCMS.Admin.Careless/FluentCMS.Admin.Careless.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../../../src/FluentCMS.Sdk/FluentCMS.Sdk.csproj" />
                <ProjectReference Include="../FluentCMS.Admin.Hosting/FluentCMS.Admin.Hosting.csproj" />
                <PackageReference Include="FluentCMS.Data.Sqlite" PrivateAssets="all" />
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

        // A half outside admin/src/ that builds on its server half.
        var half = ProjectFile.FromXml("samples/Reviews.Admin/Reviews.Admin.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../Reviews/Reviews.csproj" />
              </ItemGroup>
            </Project>
            """);
        var server = ProjectFile.FromXml("samples/Reviews/Reviews.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../../src/FluentCMS.Sdk/FluentCMS.Sdk.csproj" />
              </ItemGroup>
            </Project>
            """);

        BrowserReferencesToServerPackages(With(half, server)).ShouldBe(["Reviews.Admin references FluentCMS.Sdk"]);
    }

    // The same rule on the compiled assemblies, which also hold what a project file does not show: a <Reference> with a HintPath,
    // or a reference added by a Directory.Build.props or .targets file.
    [Fact]
    public void NoAdminAssemblyThatRunsInTheBrowserIsCompiledAgainstAServerPackage()
    {
        browserAssemblies.Value.Select(assembly => assembly.GetName().Name).ShouldContain(Admin);
        browserAssemblies.Value.SelectMany(ServerReferences).ShouldBeEmpty();

        ServerReferences(typeof(AdminRulesTests).Assembly).ShouldContain($"FluentCMS.Architecture.Tests references {Core}");
    }

    // Spec 2.3: a half builds on the contract and the wrapped set alone, so swapping the component library touches
    // FluentCMS.Admin.Components only.
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
              </ItemGroup>
            </Project>
            """);

        // The shell reached through a project of another name.
        var indirect = ProjectFile.FromXml("samples/Ratings.Admin/Ratings.Admin.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../Ratings.Shared/Ratings.Shared.csproj" />
              </ItemGroup>
            </Project>
            """);
        var shared = ProjectFile.FromXml("samples/Ratings.Shared/Ratings.Shared.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../../admin/src/FluentCMS.Admin/FluentCMS.Admin.csproj" />
              </ItemGroup>
            </Project>
            """);

        HalfViolations(With(careless, indirect, shared)).ShouldBe(
            ["FluentCMS.Plugins.Reviews.Admin references FluentCMS.Admin", "Ratings.Admin references FluentCMS.Admin"],
            ignoreOrder: true);
    }

    // Spec 6.2: the admin half of the fluentcms-plugin template is found as a half, and among the FluentCMS packages it
    // references the contract and the wrapped set only, and no project, so never its server half.
    [Fact]
    public void ThePluginTemplatesAdminHalfReferencesTheAdminAbstractionsAndTheWrappedComponentsOnly()
    {
        var half = ProjectFile.Load(TemplateHalf);

        Halves(ProjectFile.RepositoryProjects).Select(project => project.Location).ShouldContain(TemplateHalf);
        half.ProjectReferences.ShouldBeEmpty();
        half.PackageReferences.Select(package => package.Name).Where(name => name.StartsWith("FluentCMS.", StringComparison.Ordinal))
            .ShouldBe([AdminAbstractions, AdminComponents], ignoreOrder: true);
    }

    // Spec 2.3: only the wrapped set compiles against the component library, which is what FluentCMS.Admin.Components
    // references. A package of the library is matched by its id and the ids below it, and through any project but the wrapped set.
    [Fact]
    public void OnlyTheWrappedSetReferencesTheComponentLibrary()
    {
        LibraryReferences(ProjectFile.RepositoryProjects).ShouldBeEmpty();

        var half = ProjectFile.FromXml(HalvesFolder + "FluentCMS.Plugins.Reviews.Admin/FluentCMS.Plugins.Reviews.Admin.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../../FluentCMS.Admin.Abstractions/FluentCMS.Admin.Abstractions.csproj" />
                <ProjectReference Include="../../FluentCMS.Admin.Components/FluentCMS.Admin.Components.csproj" />
                <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components.Emoji" />
              </ItemGroup>
            </Project>
            """);
        var indirect = ProjectFile.FromXml("samples/Ratings.Admin/Ratings.Admin.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../../admin/src/FluentCMS.Admin.Abstractions/FluentCMS.Admin.Abstractions.csproj" />
                <ProjectReference Include="../Ratings.Shared/Ratings.Shared.csproj" />
              </ItemGroup>
            </Project>
            """);
        var shared = ProjectFile.FromXml("samples/Ratings.Shared/Ratings.Shared.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" />
              </ItemGroup>
            </Project>
            """);
        var shell = ProjectFile.FromXml(AdminFolder + "FluentCMS.Admin/FluentCMS.Admin.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../FluentCMS.Admin.Abstractions/FluentCMS.Admin.Abstractions.csproj" />
                <ProjectReference Include="../FluentCMS.Admin.Components/FluentCMS.Admin.Components.csproj" />
                <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components.Icons" />
              </ItemGroup>
            </Project>
            """);

        LibraryReferences(With(half, indirect, shared, shell)).ShouldBe(
            [
                "FluentCMS.Plugins.Reviews.Admin references Microsoft.FluentUI.AspNetCore.Components.Emoji",
                "Ratings.Admin references Microsoft.FluentUI.AspNetCore.Components",
                "FluentCMS.Admin references Microsoft.FluentUI.AspNetCore.Components.Icons",
                "FluentCMS.Admin.App references Microsoft.FluentUI.AspNetCore.Components.Icons",
            ],
            ignoreOrder: true);
    }

    // The same on the compiled assemblies: no type outside the wrapped set uses a type of the library.
    [Fact]
    public void OnlyTheWrappedSetUsesTheTypesOfTheComponentLibrary()
    {
        LibraryRule().Check(browser.Value);

        // Without the exemption the rule finds the wrapped set, and only it.
        var inWrappedSet = browser.Value.Types.Where(type => type.Assembly.Name.Split(',')[0] == AdminComponents)
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
        var wrapped = Violators(LibraryRule(exempt: null), browser.Value);
        wrapped.ShouldNotBeEmpty();
        wrapped.ShouldAllBe(type => inWrappedSet.Contains(type));

        Violators(LibraryRule(), careless.Value).ShouldBe([typeof(CarelessLibraryUser).FullName!]);
    }

    // A half is found by its folder, by its name and by what it references, so that one outside admin/src/plugins/, in a sample
    // or a template, or one placed directly under admin/src/, is held to the rules too.
    [Fact]
    public void AnAdminHalfIsFoundByItsFolderItsNameOrItsReferences()
    {
        var byFolder = ProjectFile.FromXml(HalvesFolder + "Reviews/Reviews.csproj", "<Project />");
        var byName = ProjectFile.FromXml("samples/Reviews.Admin/Reviews.Admin.csproj", "<Project />");
        var byNameInAdmin = ProjectFile.FromXml(AdminFolder + "FluentCMS.Plugins.Reviews.Admin/FluentCMS.Plugins.Reviews.Admin.csproj", "<Project />");
        var byReference = ProjectFile.FromXml("samples/Reviews.Pages/Reviews.Pages.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <PackageReference Include="FluentCMS.Admin.Components" />
              </ItemGroup>
            </Project>
            """);
        var byReferenceInAdmin = ProjectFile.FromXml(AdminFolder + "Reviews.Pages/Reviews.Pages.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../FluentCMS.Admin.Abstractions/FluentCMS.Admin.Abstractions.csproj" />
              </ItemGroup>
            </Project>
            """);
        var server = ProjectFile.FromXml("samples/Reviews/Reviews.csproj", "<Project />");

        Halves([.. ProjectFile.RepositoryProjects, byFolder, byName, byNameInAdmin, byReference, byReferenceInAdmin, server])
            .Select(half => half.Location)
            .ShouldBe([TemplateHalf, MediaHalf, byFolder.Location, byName.Location, byNameInAdmin.Location, byReference.Location, byReferenceInAdmin.Location], ignoreOrder: true);

        // Placed directly under admin/src/, a half that uses the shell and the library still breaks the rules.
        var careless = ProjectFile.FromXml(byNameInAdmin.Location, """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <ProjectReference Include="../FluentCMS.Admin/FluentCMS.Admin.csproj" />
                <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" />
              </ItemGroup>
            </Project>
            """);

        HalfViolations(With(careless)).ShouldBe(["FluentCMS.Plugins.Reviews.Admin references FluentCMS.Admin"]);
        LibraryReferences(With(careless)).ShouldBe(["FluentCMS.Plugins.Reviews.Admin references Microsoft.FluentUI.AspNetCore.Components"]);
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

        // Every admin project builds on the contract, so a reference from it reaches them all, the template's half too.
        var careless = ProjectFile.FromXml(AdminFolder + "FluentCMS.Admin.Abstractions/FluentCMS.Admin.Abstractions.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../../../src/FluentCMS.Core/FluentCMS.Core.csproj" />
              </ItemGroup>
            </Project>
            """);

        AdminProjectsReachingCore(With(careless)).ShouldBe(
            [AdminHosting, Admin, AdminAbstractions, AdminApp, AdminComponents, AdminRichText, "FluentCMS.Plugins.Media.Admin", "FluentCms-Plugin1.Admin"],
            ignoreOrder: true);

        // That no assembly of the browser references the hosting package is checked by the first rule, which does not count
        // the hosting package as one.
        BrowserProjects(ProjectFile.RepositoryProjects).Select(project => project.Name).ShouldNotContain(AdminHosting);
    }

    // Spec 3.1: a type injects or takes IJSRuntime, an IJSObjectReference, the in-process runtime or the [JSImport] host only if
    // it is on the list. A component's @inject compiles to an [Inject] property, which the scan of the assembly sees.
    [Fact]
    public void OnlyTheAllowListedTypesUseJavaScriptInterop()
    {
        InteropRule(InteropTypes).Check(browser.Value);

        // Without the list the rule finds the listed type, and only it with its nested types, such as DocumentInterop+FocusOpener.
        var listed = Violators(InteropRule([]), browser.Value);
        listed.ShouldContain(InteropTypes[0]);
        listed.ShouldAllBe(type => InteropTypes.Any(name => type == name || type.StartsWith(name + "+", StringComparison.Ordinal)));

        Violators(InteropRule(InteropTypes), careless.Value).ShouldBe(
            [typeof(CarelessInterop).FullName!, typeof(CarelessComponent).FullName!, typeof(CarelessModule).FullName!, typeof(CarelessHost).FullName!],
            ignoreOrder: true);
    }

    [Fact]
    public void EachAllowListedInteropTypeIsInTheShellTheRichTextEditorOrTheMediaHalf()
    {
        foreach (var name in InteropTypes)
        {
            var type = browser.Value.Types.SingleOrDefault(type => type.FullName == name);
            type.ShouldNotBeNull($"{name} is on the interop list but is not a type of the admin.");
            type.Assembly.Name.Split(',')[0].ShouldBeOneOf(Admin, AdminRichText, MediaHalfAssembly);
        }
    }

    // An admin project or half under admin/src/ that the test project does not reference would escape the scans of the
    // assemblies. A half of a template or a sample, built from packages, is held to the project rules only.
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

    private static IEnumerable<string> ServerReferences(Assembly assembly)
    {
        var allowed = BrowserProjects(ProjectFile.RepositoryProjects).Select(project => project.Name).Append(Client).ToHashSet(StringComparer.Ordinal);

        return assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("FluentCMS.", StringComparison.Ordinal) && !allowed.Contains(name))
            .Select(name => $"{assembly.GetName().Name} references {name}");
    }

    private static TypesShouldConjunction InteropRule(string[] allowed)
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

    private static TypesShouldConjunction LibraryRule(string? exempt = AdminComponents) =>
        Types().That().FollowCustomPredicate(
                type => exempt is null || type.Assembly.Name.Split(',')[0] != exempt,
                "are not in the wrapped set")
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow(LibraryNamespace);

    private static List<string> Violators(TypesShouldConjunction rule, ArchUnitNET.Domain.Architecture architecture) =>
        [.. rule.Evaluate(architecture).Where(result => !result.Passed).Select(result => result.EvaluatedObjectIdentifier.Identifier).Distinct()];

    // Every project under admin/src/ but the hosting package, which runs in the ASP.NET Core host, and every half found elsewhere.
    private static List<ProjectFile> BrowserProjects(IReadOnlyList<ProjectFile> projects) =>
        [.. projects.Where(project => project.Location.StartsWith(AdminFolder, StringComparison.Ordinal) && project.Name != AdminHosting)
            .Union(Halves(projects))];

    private static List<ProjectFile> Halves(IReadOnlyList<ProjectFile> projects) =>
        [.. projects.Where(project => project.Location.StartsWith(HalvesFolder, StringComparison.Ordinal)
            || (!IsAdminPackage(project) && (project.Name.EndsWith(".Admin", StringComparison.Ordinal)
                || DirectReferences(project).Any(reference => reference is AdminAbstractions or AdminComponents))))];

    private static bool IsAdminPackage(ProjectFile project) => AdminPackages.Contains(project.Name, StringComparer.Ordinal);

    private static List<string> BrowserReferencesToServerPackages(IReadOnlyList<ProjectFile> projects)
    {
        var browserProjects = BrowserProjects(projects);
        var browserNames = browserProjects.Select(project => project.Name).ToHashSet(StringComparer.Ordinal);

        return [.. browserProjects.SelectMany(project => Reachable(project, projects)
            .Where(reference => reference.StartsWith("FluentCMS.", StringComparison.Ordinal) && !browserNames.Contains(reference) && reference != Client)
            .Select(reference => $"{project.Name} references {reference}"))];
    }

    // What a half reaches other than through the contract and the wrapped set.
    private static List<string> HalfViolations(IReadOnlyList<ProjectFile> projects) =>
        [.. Halves(projects).SelectMany(half => Reachable(half, projects, [AdminAbstractions, AdminComponents])
            .Where(reference => reference.StartsWith("FluentCMS.", StringComparison.Ordinal))
            .Select(reference => $"{half.Name} references {reference}"))];

    // A package of the library that a browser project reaches other than through the wrapped set.
    private static List<string> LibraryReferences(IReadOnlyList<ProjectFile> projects)
    {
        var library = projects.Single(project => project.Name == AdminComponents).PackageReferences
            .Where(package => !package.DevelopmentOnly && !package.Name.StartsWith("FluentCMS.", StringComparison.Ordinal))
            .Select(package => package.Name)
            .ToList();

        library.ShouldContain(Library);

        return [.. BrowserProjects(projects).Where(project => project.Name != AdminComponents)
            .SelectMany(project => Reachable(project, projects, [AdminComponents])
                .Where(reference => library.Any(id => reference == id || reference.StartsWith(id + ".", StringComparison.Ordinal)))
                .Select(reference => $"{project.Name} references {reference}"))];
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

        return [.. projects.Where(project => project.Location.StartsWith(AdminFolder, StringComparison.Ordinal) || halves.Contains(project))
            .Where(project => Reachable(project, projects).Contains(Core))
            .Select(project => project.Name)];
    }

    // A development-only package is left out, but a FluentCMS one only when it runs in the compiler: with PrivateAssets="all",
    // FluentCMS.Core is still compiled against.
    private static IEnumerable<string> DirectReferences(ProjectFile project) =>
        project.ProjectReferences.Concat(project.PackageReferences
            .Where(package => !package.DevelopmentOnly
                || (package.Name.StartsWith("FluentCMS.", StringComparison.Ordinal) && !CompilerPackages.Contains(package.Name, StringComparer.Ordinal)))
            .Select(package => package.Name));

    // What a project references, and what the projects of the repository it references do in turn; a package of the repository
    // is its project. A name in `through` is neither counted nor followed.
    private static HashSet<string> Reachable(ProjectFile project, IReadOnlyList<ProjectFile> projects, string[]? through = null)
    {
        var byName = projects.ToDictionary(other => other.Name, StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(DirectReferences(project));

        while (pending.TryPop(out var name))
        {
            if (through?.Contains(name, StringComparer.Ordinal) != true && reached.Add(name) && byName.TryGetValue(name, out var referenced))
            {
                foreach (var next in DirectReferences(referenced))
                {
                    pending.Push(next);
                }
            }
        }

        return reached;
    }

    private static List<ProjectFile> With(params ProjectFile[] replacements) =>
        [.. ProjectFile.RepositoryProjects.Where(project => !replacements.Any(replacement => replacement.Name == project.Name)), .. replacements];

    private static bool IsExtensionsAbstractions(string packageName) =>
        packageName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
        && packageName.EndsWith(".Abstractions", StringComparison.Ordinal);
}
