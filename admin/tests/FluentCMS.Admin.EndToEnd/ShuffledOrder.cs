using FluentCMS.Admin.EndToEnd;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestClassOrderer(typeof(ShuffledOrder))]
[assembly: TestMethodOrderer(typeof(ShuffledOrder))]

namespace FluentCMS.Admin.EndToEnd;

// The browser tests share one collection, whose classes and cases xUnit.net runs in an order of its own that no seed
// changes. With FLUENTCMS_E2E_SHUFFLE set to a number, the classes and the cases of each class run in an order made from it,
// so that a run shows whether a case reads what another wrote (#3314); without it the order is xUnit.net's.
public sealed class ShuffledOrder : ITestClassOrderer, ITestMethodOrderer
{
    public const string Variable = "FLUENTCMS_E2E_SHUFFLE";

    public IReadOnlyCollection<TTestClass?> OrderTestClasses<TTestClass>(IReadOnlyCollection<TTestClass?> testClasses)
        where TTestClass : ITestClass =>
        Seed() is { } seed
            ? Shuffle(testClasses, testClass => testClass?.TestClassName ?? "", seed)
            : DefaultTestClassOrderer.Instance.OrderTestClasses(testClasses);

    public IReadOnlyCollection<TTestMethod?> OrderTestMethods<TTestMethod>(IReadOnlyCollection<TTestMethod?> testMethods)
        where TTestMethod : ITestMethod =>
        Seed() is { } seed
            ? Shuffle(testMethods, testMethod => testMethod?.MethodName ?? "", seed)
            : DefaultTestMethodOrderer.Instance.OrderTestMethods(testMethods);

    private static int? Seed() =>
        int.TryParse(Environment.GetEnvironmentVariable(Variable), out var seed) ? seed : null;

    // Sorted by name first, so that the same seed gives the same order whatever order the items came in.
    private static List<T> Shuffle<T>(IEnumerable<T> items, Func<T, string> name, int seed)
    {
        var random = new Random(seed);
        return [.. items.OrderBy(name, StringComparer.Ordinal).Select(item => (Item: item, Key: random.Next())).OrderBy(pair => pair.Key).Select(pair => pair.Item)];
    }
}
