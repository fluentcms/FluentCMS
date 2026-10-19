using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentCMS.Core.Extensions;

namespace FluentCMS.Tests.Extensions;

// EXT-N1 and EXT-N2: every extension point has a conformance class, and its members match the committed baseline, so a change to any
// of them is seen in review and called additive or breaking there (ADR-0013).
public sealed partial class ExtensionPointRulesTests
{
    private const string UpdateVariable = "FLUENTCMS_UPDATE_EXTENSION_BASELINE";

    private static readonly NullabilityInfoContext Nullability = new();

    private static IReadOnlyList<Type> ExtensionPoints { get; } = [.. typeof(ExtensionPointAttribute).Assembly.GetTypes()
        .Where(type => type.IsInterface && type.IsDefined(typeof(ExtensionPointAttribute), inherit: false))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    private static string BaselinePath => Path.Combine(ProjectReferenceRulesTests.FindRepositoryRoot(), "tests", "FluentCMS.Tests", "Extensions", "extension-points.txt");

    [Fact]
    public void Every_extension_point_has_a_conformance_class()
    {
        var root = ProjectReferenceRulesTests.FindRepositoryRoot();
        var covered = Directory.GetFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => ConformanceFor().Matches(File.ReadAllText(file)))
            .SelectMany(attribute => TypeOf().Matches(attribute.Value).Select(type => type.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(ExtensionPoints);
        Assert.Empty(ExtensionPoints.Where(type => !covered.Contains(type.Name)).Select(type => $"{type.FullName} has no class marked [ConformanceFor(typeof({type.Name}))]"));
    }

    [Fact]
    public void Every_extension_point_matches_its_baseline()
    {
        var actual = Surface();
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(BaselinePath, actual);
        }

        var expected = File.ReadAllText(BaselinePath).ReplaceLineEndings("\n");
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var removed = expectedLines.Except(actualLines, StringComparer.Ordinal).Select(line => $"- {line}");
        var added = actualLines.Except(expectedLines, StringComparer.Ordinal).Select(line => $"+ {line}");
        var changes = removed.Concat(added).ToList();

        Assert.True(
            changes.Count == 0,
            "An extension point changed:\n" + string.Join('\n', changes) + "\n"
            + "Update tests/FluentCMS.Tests/Extensions/extension-points.txt in the same pull request, and say there whether the change is "
            + $"additive or breaking (ADR-0013). Running the tests with {UpdateVariable}=1 rewrites it.");
    }

    // One line per extension point and one per member, in a stable order, with the nullability of each type.
    private static string Surface()
    {
        var text = new StringBuilder();
        foreach (var type in ExtensionPoints)
        {
            text.Append(type.FullName).Append('\n');

            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(Describe)
                .OfType<string>()
                .Order(StringComparer.Ordinal);
            foreach (var member in members)
            {
                text.Append("  ").Append(member).Append('\n');
            }
        }

        return text.ToString();
    }

    private static string? Describe(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo property:
                var accessors = (property.GetMethod is not null ? "get; " : string.Empty) + (property.SetMethod is not null ? "set; " : string.Empty);
                var defaulted = property.GetMethod is { IsAbstract: false } ? " (default)" : string.Empty;
                return $"{property.Name}: {Name(Nullability.Create(property))} {{ {accessors}}}{defaulted}";
            case MethodInfo { IsSpecialName: false } method:
                var parameters = string.Join(", ", method.GetParameters().Select(parameter =>
                    $"{Name(Nullability.Create(parameter))} {parameter.Name}" + (parameter.HasDefaultValue ? $" = {parameter.DefaultValue ?? "default"}" : string.Empty)));
                var generics = method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>" : string.Empty;
                return $"{method.Name}{generics}({parameters}): {Name(Nullability.Create(method.ReturnParameter))}{(method.IsAbstract ? string.Empty : " (default)")}";
            default:
                return null;
        }
    }

    private static string Name(NullabilityInfo info)
    {
        var type = info.Type;
        var nullable = info.ReadState == NullabilityState.Nullable ? "?" : string.Empty;

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Simple(underlying) + "?";
        }

        if (type.IsArray)
        {
            return Name(info.ElementType!) + "[]" + nullable;
        }

        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            return $"{name}<{string.Join(", ", info.GenericTypeArguments.Select(Name))}>{nullable}";
        }

        return Simple(type) + nullable;
    }

    private static string Simple(Type type)
    {
        return type == typeof(void) ? "void" : type.Name;
    }

    [GeneratedRegex(@"\[ConformanceFor\((?:[^\[\]]|\[[^\]]*\])*\)\]")]
    private static partial Regex ConformanceFor();

    [GeneratedRegex(@"typeof\((\w+)\)")]
    private static partial Regex TypeOf();
}
