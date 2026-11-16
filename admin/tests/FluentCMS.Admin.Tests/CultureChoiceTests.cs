using System.Globalization;
using FluentCMS.Admin.Localization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class CultureChoiceTests
{
    private static readonly InterfaceCultureOptions Options = new() { Offered = ["en", "fr", "ar"], Default = "fr" };

    [Theory]
    [InlineData("ar", "en-US", "ar")]
    [InlineData("AR", "en-US", "ar")]
    [InlineData("de", "en-US", "en")]
    [InlineData("not a culture!", "en-US", "en")]
    [InlineData(null, "en-GB", "en")]
    [InlineData(null, "fr", "fr")]
    [InlineData(null, "ja-JP", "fr")]
    [InlineData("", "ja-JP", "fr")]
    public void TheCultureIsThePreferenceThenTheBrowsersThenTheDefault(string? preferred, string browser, string expected)
    {
        var culture = InterfaceCulture.Choose(preferred, CultureInfo.GetCultureInfo(browser), Options);

        culture.Name.ShouldBe(expected);
    }

    [Fact]
    public void WithoutConfigurationEnglishIsTheOnlyCulture()
    {
        var options = new InterfaceCultureOptions();

        InterfaceCulture.Choose(null, CultureInfo.GetCultureInfo("fr-FR"), options).Name.ShouldBe("en");
        options.Offered.ShouldBe(["en"]);
    }

    [Fact]
    public void ApplyingSetsTheInterfaceAndFormatCultures()
    {
        var culture = new InterfaceCulture(CultureInfo.GetCultureInfo("en"));
        var formats = CultureInfo.GetCultureInfo("en-GB");
        var ui = CultureInfo.CurrentUICulture;
        var current = CultureInfo.CurrentCulture;
        try
        {
            culture.Apply(formats);

            CultureInfo.CurrentUICulture.Name.ShouldBe("en");
            CultureInfo.CurrentCulture.Name.ShouldBe("en-GB");
            CultureInfo.DefaultThreadCurrentUICulture!.Name.ShouldBe("en");
            CultureInfo.DefaultThreadCurrentCulture!.Name.ShouldBe("en-GB");
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = null;
            CultureInfo.DefaultThreadCurrentCulture = null;
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.CurrentCulture = current;
        }
    }
}
