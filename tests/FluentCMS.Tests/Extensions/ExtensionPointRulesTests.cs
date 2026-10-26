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
        var actual = Surface(ExtensionPoints);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(BaselinePath, actual);
        }

        var changes = Changes(File.ReadAllText(BaselinePath), actual);

        Assert.True(
            changes.Count == 0,
            "An extension point changed:\n" + string.Join('\n', changes) + "\n"
            + "Update tests/FluentCMS.Tests/Extensions/extension-points.txt in the same pull request, and say there whether the change is "
            + $"additive or breaking (ADR-0013). Running the tests with {UpdateVariable}=1 rewrites it.");
    }

    // Several extension points have a member of the same name, so a member moved from one to another is a change too.
    [Fact]
    public void A_member_is_compared_within_the_extension_point_it_belongs_to()
    {
        const string Expected = "First\n  Name: String { get; }\nSecond\n  Name: String { get; }\nThird\n";
        const string Actual = "First\n  Name: String { get; }\nSecond\nThird\n  Name: String { get; }\n";

        Assert.Equal(["- Second  Name: String { get; }", "+ Third  Name: String { get; }"], Changes(Expected, Actual));
    }

    [Fact]
    public void An_event_is_a_member_of_the_surface()
    {
        Assert.Contains("  event Changed: EventHandler\n", Surface([typeof(IWithAnEvent)]), StringComparison.Ordinal);
    }

    // Each member line is read with the extension point above it, and each change names both.
    private static List<string> Changes(string expected, string actual)
    {
        var before = Qualified(expected);
        var after = Qualified(actual);
        return [.. before.Except(after, StringComparer.Ordinal).Select(line => $"- {line}"), .. after.Except(before, StringComparer.Ordinal).Select(line => $"+ {line}")];
    }

    private static List<string> Qualified(string surface)
    {
        var lines = new List<string>();
        var owner = string.Empty;
        foreach (var line in surface.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("  ", StringComparison.Ordinal))
            {
                lines.Add(owner + line);
            }
            else
            {
                owner = line;
                lines.Add(line);
            }
        }

        return lines;
    }

    // One line per extension point and one per member, in a stable order, with the nullability of each type.
    private static string Surface(IEnumerable<Type> extensionPoints)
    {
        var text = new StringBuilder();
        foreach (var type in extensionPoints)
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
            case EventInfo @event:
                return $"event {@event.Name}: {Name(Nullability.Create(@event))}{(@event.AddMethod is { IsAbstract: false } ? " (default)" : string.Empty)}";
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

    private interface IWithAnEvent
    {
        event EventHandler Changed;
    }
}
