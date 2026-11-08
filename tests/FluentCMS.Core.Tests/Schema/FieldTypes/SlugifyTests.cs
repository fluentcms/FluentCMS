using System.Text;
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
    [InlineData("你好", "你好")]
    [InlineData("日本語のタイトル", "日本語のタイトル")]
    [InlineData("コーヒー 2026", "コーヒー-2026")]
    [InlineData("한국어 제목", "한국어-제목")]
    [InlineData("𠮷野家", "𠮷野家")]
    [InlineData("Привет, Мир!", "привет-мир")]
    [InlineData("Ελληνικά", "ελληνικα")]
    [InlineData("مرحبا بالعالم", "مرحبا-بالعالم")]
    [InlineData("١٢٣ أرقام", "١٢٣-ارقام")]
    [InlineData("Hello 世界, Мир", "hello-世界-мир")]
    [InlineData("Azərbaycan", "azərbaycan")]
    [InlineData("x\U0001D400y", "x-y")]
    public void KeepsTheLettersAndDigitsOfEveryScript(string source, string expected)
    {
        var slug = SlugFieldType.Slugify(source);

        slug.ShouldBe(expected);
        TestFields.Coerce("slug", slug).ShouldBe(expected);
    }

    [Theory]
    [InlineData("école", "ecole")]
    [InlineData("Ёлка", "елка")]
    [InlineData("مَرْحَبًا", "مرحبا")]
    [InlineData("שָׁלוֹם", "שלום")]
    [InlineData("हिन्दी", "हनद")]
    [InlineData("a⃝b", "ab")]
    [InlineData("مـرحـبا", "مرحبا")]
    [InlineData("aboutㅤ", "about")]
    [InlineData("한ﾠ국", "한국")]
    public void CombiningMarksAndFillersAreDroppedInEveryScript(string source, string expected)
    {
        var slug = SlugFieldType.Slugify(source);

        slug.ShouldBe(expected);
        TestFields.Coerce("slug", slug).ShouldBe(expected);
    }

    [Fact]
    public void EveryLetterOrDigitGivesASlugTheFieldAcceptsAndThatSlugifiesToItself()
    {
        var type = TestFields.Type("slug");
        var field = TestFields.Field("slug");
        List<string> failures = [];
        for (var value = 0; value <= 0x10FFFF; value++)
        {
            if (!Rune.TryCreate(value, out var rune) || !Rune.IsLetterOrDigit(rune))
            {
                continue;
            }

            var slug = SlugFieldType.Slugify(rune.ToString());
            if (slug.Length > 0 && (SlugFieldType.Slugify(slug) != slug || !type.TryCoerce(slug, field, out _, out _)))
            {
                failures.Add($"U+{value:X4}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("🎉 ✨")]
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
    [InlineData("𠮷𠮷", 3, "𠮷")]
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
