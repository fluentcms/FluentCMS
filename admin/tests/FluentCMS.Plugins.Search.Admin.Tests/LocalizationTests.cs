using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Search.Admin.Tests;

// The sweep of spec 7: every page of the half under both pseudo-cultures, the names of the types marked as data.
public sealed class LocalizationTests : SearchTestContext
{
    public static TheoryData<string, string> Pages() => PseudoSweep.Pages(typeof(SearchPage).Assembly);

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTextOfEveryPageComesFromTheResources(string culture, string page)
    {
        this.UsePseudoCulture(culture);
        Schema.Label = name => PseudoCulture.Transform(name, culture);

        var cut = page switch
        {
            nameof(SearchPage) => Render<SearchPage>(),
            _ => throw PseudoSweep.NoSweep(page),
        };

        cut.WaitForAssertion(() => cut.FindAll("select option").Count.ShouldBe(3));
        cut.ShouldBeLocalized();
    }
}
