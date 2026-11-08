using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

public class StringFieldTypeTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("longText")]
    public void TextAcceptsStrings(string type)
    {
        TestFields.Coerce(type, "hello").ShouldBe("hello");
        TestFields.Coerce(type, TestFields.Json("\"hello\"")).ShouldBe("hello");
        TestFields.Coerce(type, 'c').ShouldBe("c");
        TestFields.Coerce(type, "").ShouldBe("");
        TestFields.Coerce(type, null).ShouldBeNull();
        TestFields.Coerce(type, TestFields.Json("null")).ShouldBeNull();
    }

    [Theory]
    [InlineData("text")]
    [InlineData("longText")]
    [InlineData("email")]
    [InlineData("url")]
    [InlineData("slug")]
    public void StringTypesRejectOtherKinds(string type)
    {
        TestFields.Reject(type, 5).ShouldBe("expected a string but got a number");
        TestFields.Reject(type, true).ShouldBe("expected a string but got a boolean");
        TestFields.Reject(type, TestFields.Json("12")).ShouldBe("expected a string but got a number");
        TestFields.Reject(type, TestFields.Json("{}")).ShouldBe("expected a string but got an object");
        TestFields.Reject(type, TestFields.Json("[\"a\"]")).ShouldBe("expected a string but got an array");
    }

    [Fact]
    public void TextDoesNotApplyLengthLimits()
    {
        TestFields.Coerce("text", "far too long", field => field.Length(null, 3)).ShouldBe("far too long");
    }

    [Theory]
    [InlineData("a@b.com")]
    [InlineData("first.last+tag@example.co.uk")]
    [InlineData("User@Example.COM")]
    public void EmailAcceptsAddresses(string address)
    {
        TestFields.Coerce("email", address).ShouldBe(address);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    [InlineData("Name <a@b.com>")]
    [InlineData("a b@c.com")]
    [InlineData(" a@b.com")]
    [InlineData("a@b.com ")]
    [InlineData("")]
    public void EmailRejectsOtherText(string text)
    {
        TestFields.Reject("email", text).ShouldBe("is not a valid email address");
    }

    [Theory]
    [InlineData("https://example.com/a?b=1")]
    [InlineData("http://localhost:5000")]
    [InlineData("HTTPS://Example.com")]
    public void UrlAcceptsAbsoluteHttpAndHttps(string text)
    {
        TestFields.Coerce("url", text).ShouldBe(text);
    }

    [Fact]
    public void UrlAcceptsAUriAndReturnsItsText()
    {
        TestFields.Coerce("url", new Uri("https://example.com/a")).ShouldBe("https://example.com/a");
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.com")]
    [InlineData("https://")]
    [InlineData("")]
    public void UrlRejectsOtherText(string text)
    {
        TestFields.Reject("url", text).ShouldBe("is not an absolute http or https URL");
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("hello-world")]
    [InlineData("a1-b2-c3")]
    [InlineData("2026")]
    [InlineData("привет-мир")]
    [InlineData("日本語-2026")]
    [InlineData("مرحبا")]
    [InlineData("𠮷野家")]
    [InlineData("héllo")]
    public void SlugAcceptsLowerCaseWordsWithSingleHyphens(string text)
    {
        TestFields.Coerce("slug", text).ShouldBe(text);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("Привет")]
    [InlineData("hello--world")]
    [InlineData("-hello")]
    [InlineData("hello-")]
    [InlineData("hello world")]
    [InlineData("日本 語")]
    [InlineData("hello_world")]
    [InlineData("héllo")]
    [InlineData("한")]
    [InlineData("aboutㅤ")]
    [InlineData("مـرحبا")]
    [InlineData("")]
    public void SlugRejectsOtherText(string text)
    {
        TestFields.Reject("slug", text).ShouldStartWith("is not a slug");
    }

    [Fact]
    public void SlugRejectsAnUnpairedSurrogate()
    {
        var text = new string(['a', '\uD842']);

        TestFields.Reject("slug", text).ShouldStartWith("is not a slug");
    }
}
