using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.Api.Account;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Account;

// The colours and the logo go into the hosted pages' style sheet and markup, so only what cannot break out of them is kept.
public sealed class BrandingSettingsTests
{
    [Theory]
    [InlineData("#fff", true)]
    [InlineData("#2563EB", true)]
    [InlineData("#2563eb80", false)]
    [InlineData("red", false)]
    [InlineData("#fff; } body { display: none", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AColourIsAHexColour(string? value, bool valid) => BrandingSettings.IsColor(value).ShouldBe(valid);

    [Theory]
    [InlineData("https://cdn.example.com/logo.svg", true)]
    [InlineData("/media/logo.png", true)]
    [InlineData("http://cdn.example.com/logo.svg", false)]
    [InlineData("//evil.example/logo.svg", false)]
    [InlineData("/\\evil.example/logo.svg", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/svg+xml,<svg/>", false)]
    [InlineData("logo.png", false)]
    public void ALogoIsAnHttpsUrlOrAPathOfThisHost(string value, bool valid) => BrandingSettings.IsLogoUrl(value).ShouldBe(valid);

    [Fact]
    public void TheDefaultsAreValid()
    {
        var settings = new BrandingSettings();

        Validator.TryValidateObject(settings, new ValidationContext(settings), [], validateAllProperties: true).ShouldBeTrue();
        settings.Name.ShouldBe("FluentCMS");
        settings.LogoUrl.ShouldBeNull();
    }

    [Fact]
    public void ABadLogoOrColourFailsValidation()
    {
        var settings = new BrandingSettings { LogoUrl = "javascript:alert(1)", PrimaryColor = "red" };
        List<ValidationResult> results = [];

        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true).ShouldBeFalse();
        results.SelectMany(result => result.MemberNames).ShouldContain(nameof(BrandingSettings.PrimaryColor));

        settings.PrimaryColor = BrandingSettings.DefaultPrimaryColor;
        results.Clear();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true).ShouldBeFalse();
        results.ShouldHaveSingleItem().MemberNames.ShouldBe([nameof(BrandingSettings.LogoUrl)]);
    }
}
