using FluentCMS.Core.Schema.FieldTypes;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

public class SlugifyTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("  Hello   World  ", "hello-world")]
    [InlineData("Crème Brûlée", "creme-brulee")]
    [InlineData("Ünïcödé", "unicode")]
    [InlineData("Straße", "strasse")]
    [InlineData("Ørsted Æsir", "orsted-aesir")]
    [InlineData("Hello, World! It's 2026.", "hello-world-it-s-2026")]
    [InlineData("a---b___c", "a-b-c")]
    [InlineData("--leading and trailing--", "leading-and-trailing")]
    [InlineData("Already-a-slug", "already-a-slug")]
    [InlineData("école", "ecole")]
    public void NormalisesTextToASlug(string source, string expected)
    {
        var slug = SlugFieldType.Slugify(source);

        slug.ShouldBe(expected);
        TestFields.Coerce("slug", slug).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("你好")]
    public void ASourceWithoutLettersOrDigitsGivesAnEmptySlug(string source)
    {
        SlugFieldType.Slugify(source).ShouldBe("");
    }

    [Theory]
    [InlineData("hello-world", 5, "hello")]
    [InlineData("hello-world", 6, "hello")]
    [InlineData("hello-world", 7, "hello-w")]
    [InlineData("hello-world", 11, "hello-world")]
    [InlineData("hello-world", 50, "hello-world")]
    [InlineData("hello-world", 0, "")]
    public void TheMaximumLengthBoundsTheSlugWithoutATrailingHyphen(string source, int maxLength, string expected)
    {
        SlugFieldType.Slugify(source, maxLength).ShouldBe(expected);
    }

    [Fact]
    public void ANullSourceIsRejected()
    {
        Should.Throw<ArgumentNullException>(() => SlugFieldType.Slugify(null!));
    }

    [Fact]
    public void TheResultDoesNotDependOnTheCurrentCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            SlugFieldType.Slugify("TITLE Is Ice").ShouldBe("title-is-ice");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
