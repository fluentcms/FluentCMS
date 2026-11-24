using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Architecture.Tests;

// docs/admin-configuration.md is what a team outside the repository reads for config.json and FluentCms:Admin, so it names
// every member of AdminConfiguration and every key of AdminOptions, and none they do not have.
public sealed partial class AdminConfigurationGuideTests
{
    private static readonly string Guide = File.ReadAllText(Path.Combine(ProjectFile.FindRepositoryRoot(), "docs", "admin-configuration.md"));

    [Fact]
    public void TheTableOfConfigJsonHasARowForEachMemberOfAdminConfiguration()
    {
        var rows = MemberRow().Matches(Guide).Select(row => (row.Groups["member"].Value, row.Groups["property"].Value));

        rows.ShouldBe(Members(typeof(AdminConfiguration), "", ""), ignoreOrder: true);
    }

    [Fact]
    public void TheGuideNamesEveryPropertyOfAdminConfigurationThatIsNoMember()
    {
        var computed = typeof(AdminConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is null)
            .Select(property => property.Name)
            .ToList();

        computed.ShouldNotBeEmpty();
        computed.ShouldAllBe(name => Guide.Contains($"`{name}`", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGuideNamesEveryKeyOfAdminOptionsAndNoOther()
    {
        var named = AdminKey().Matches(Guide).Select(key => key.Value).Distinct(StringComparer.Ordinal);

        named.ShouldBe(Keys(typeof(AdminOptions), $"FluentCms:{AdminOptions.Section}"), ignoreOrder: true);
    }

    // The members config.json has, as its JSON path and the property's path: a record of the same namespace is an object of
    // members, and a property without a setter is computed, not read from the file.
    private static IEnumerable<(string Member, string Property)> Members(Type type, string member, string property) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(info => info.SetMethod is not null)
            .SelectMany(info => IsGroup(info.PropertyType, typeof(AdminConfiguration))
                ? Members(info.PropertyType, member + JsonNamingPolicy.CamelCase.ConvertName(info.Name) + ".", property + info.Name + ".")
                : [(member + JsonNamingPolicy.CamelCase.ConvertName(info.Name), property + info.Name)]);

    private static IEnumerable<string> Keys(Type type, string prefix) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(info => IsGroup(info.PropertyType, typeof(AdminOptions))
                ? Keys(info.PropertyType, $"{prefix}:{info.Name}")
                : [$"{prefix}:{info.Name}"]);

    private static bool IsGroup(Type type, Type root) => type.IsClass && type.Namespace == root.Namespace;

    // A row of the table of config.json: the member, then the property of AdminConfiguration.
    [GeneratedRegex(@"^\| `(?<member>[A-Za-z.]+)` \| `(?<property>[A-Za-z.]+)` \|", RegexOptions.Multiline)]
    private static partial Regex MemberRow();

    // A key under FluentCms:Admin, without the index of an item of a list, such as the 0 of Cultures:Offered:0.
    [GeneratedRegex(@"FluentCms:Admin(?::[A-Za-z]+)+")]
    private static partial Regex AdminKey();
}
