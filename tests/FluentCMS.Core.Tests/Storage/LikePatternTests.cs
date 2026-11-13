using FluentCMS.Core.Storage;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Storage;

public class LikePatternTests
{
    [Theory]
    [InlineData("ann", "%ann%")]
    [InlineData("Zoë O'Neil", "%Zoë O'Neil%")]
    [InlineData("50%", @"%50\%%")]
    [InlineData("ann_marie", @"%ann\_marie%")]
    [InlineData("[sales]", @"%\[sales]%")]
    [InlineData(@"c:\temp", @"%c:\\temp%")]
    [InlineData("", "%%")]
    public void EveryCharacterThatALikeReadsIsEscaped(string text, string pattern) =>
        LikePattern.Containing(text).ShouldBe(pattern);
}
