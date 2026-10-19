using System.Xml.Linq;

namespace FluentCMS.Tests;

// ADR-0008, MDL-01: every model call goes through the gateway, so no project but Infrastructure references a provider package and
// no code but the gateway and its wiring touches a chat client.
public sealed class ModelAccessRulesTests
{
    private static readonly string[] ProviderPackages = ["Anthropic", "Microsoft.Extensions.AI.OpenAI", "OpenAI"];

    private static readonly string[] ModelNamespaces = ["Microsoft.Extensions.AI", "OpenAI", "Anthropic"];

    // The gateway in Core and the provider wiring in Infrastructure.
    private static readonly string[] GatewayFolders =
    [
        Path.Combine("src", "FluentCMS.Core", "Models"),
        Path.Combine("src", "FluentCMS.Infrastructure", "Models"),
    ];

    [Fact]
    public void Only_Infrastructure_references_a_provider_package()
    {
        var root = ProjectReferenceRulesTests.FindRepositoryRoot();

        var violations = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(project => Path.GetFileNameWithoutExtension(project) != "FluentCMS.Infrastructure")
            .SelectMany(project => XDocument.Load(project).Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Where(package => ProviderPackages.Contains(package))
                .Select(package => $"{Path.GetFileNameWithoutExtension(project)} references {package}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Only_the_gateway_and_its_wiring_use_a_model_client()
    {
        var root = ProjectReferenceRulesTests.FindRepositoryRoot();
        var gateway = GatewayFolders.Select(folder => Path.Combine(root, folder) + Path.DirectorySeparatorChar).ToList();

        var violations = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !gateway.Any(folder => file.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            .Where(file => File.ReadLines(file).Any(line => ModelNamespaces.Any(ns => line.StartsWith($"using {ns};", StringComparison.Ordinal)
                || line.StartsWith($"using {ns}.", StringComparison.Ordinal))))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.Empty(violations);
    }
}
