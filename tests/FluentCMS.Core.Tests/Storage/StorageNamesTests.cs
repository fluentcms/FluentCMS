using FluentCMS.Core.Storage;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Storage;

public class StorageNamesTests
{
    private const string AltText = "ux_cms_customer_testimonial_highlight_i18n_hero_section_configuration_background_image_alt_text";
    private const string Caption = "ux_cms_customer_testimonial_highlight_i18n_hero_section_configuration_background_image_caption";

    [Theory]
    [InlineData("ux_cms_article_i18n_slug")]
    [InlineData("ux_cms_abcdefghijklmnopqrstuvwxyz_abcdefghijklmnopqrstuvwxyz_ab")]
    public void ANameThatFitsIsKept(string name) => StorageNames.Identifier(name).ShouldBe(name);

    // Pinned, because a different result renames the indexes of every database that has one.
    [Fact]
    public void ALongNameKeepsItsStartAndEndsInAHashOfTheWhole()
    {
        StorageNames.Identifier(AltText).ShouldBe("ux_cms_customer_testimonial_highlight_i18n_hero_sectio_358eeeb1");
        StorageNames.Identifier(Caption).ShouldBe("ux_cms_customer_testimonial_highlight_i18n_hero_sectio_fc2675e3");
    }

    [Fact]
    public void ALongNameFitsInPostgreSqlsBudget()
    {
        StorageNames.Identifier(new string('x', 64)).Length.ShouldBe(StorageNames.MaxIdentifierLength);
        StorageNames.Identifier(new string('x', 500)).Length.ShouldBe(StorageNames.MaxIdentifierLength);
    }

    [Fact]
    public void TheHashDoesNotFollowAnUnderscoreAtTheCut()
    {
        var name = new string('a', 53) + "_" + new string('b', 20);

        StorageNames.Identifier(name).ShouldStartWith(new string('a', 53) + "_");
        StorageNames.Identifier(name).ShouldNotContain("__");
    }
}
