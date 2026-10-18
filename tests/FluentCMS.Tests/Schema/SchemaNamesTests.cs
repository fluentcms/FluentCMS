using FluentCMS.Core.Schema;

namespace FluentCMS.Tests.Schema;

public sealed class SchemaNamesTests
{
    [Theory]
    [InlineData("price", true)]
    [InlineData("validUntil", true)]
    [InlineData("valid_until", true)]
    [InlineData("spec2", true)]
    [InlineData("Price", false)]
    [InlineData("_price", false)]
    [InlineData("2nd", false)]
    [InlineData("unit price", false)]
    [InlineData("unit-price", false)]
    [InlineData("price\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Field_names_start_with_a_lowercase_letter_and_hold_letters_digits_and_underscores(string? name, bool valid)
    {
        Assert.Equal(valid, SchemaNames.IsFieldName(name));
    }

    [Fact]
    public void Field_names_are_at_most_64_characters()
    {
        Assert.True(SchemaNames.IsFieldName(new string('a', 64)));
        Assert.False(SchemaNames.IsFieldName(new string('a', 65)));
    }

    [Theory]
    [InlineData("text", true)]
    [InlineData("richText", true)]
    [InlineData("acme.rating", true)]
    [InlineData("acme.media.gallery", true)]
    [InlineData("Text", false)]
    [InlineData("acme-rating", false)]
    [InlineData(".rating", false)]
    [InlineData("acme.Rating", false)]
    public void Field_type_names_are_camel_case_words_with_optional_dotted_prefixes(string name, bool valid)
    {
        Assert.Equal(valid, SchemaNames.IsFieldTypeName(name));
    }
}
