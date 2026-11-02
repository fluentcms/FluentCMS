using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Schema;

public class RichTextContractTests
{
    [Fact]
    public void FormatNamesAreLowerCase()
    {
        RichTextFormats.Html.ShouldBe("html");
        RichTextFormats.Markdown.ShouldBe("markdown");
    }

    [Fact]
    public void RenderOptionsResolveNothingByDefault()
    {
        var options = new RichTextRenderOptions();

        options.EntryUrlResolver.ShouldBeNull();
        options.MediaUrlResolver.ShouldBeNull();
    }
}
