using FluentCMS.Core.Agents;

namespace FluentCMS.Tests.Agents;

public sealed class ToolCallTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_declared_value_is_no_value(string? value)
    {
        Assert.Null(ToolCall.Declared(value));
    }

    [Fact]
    public void A_declared_value_is_trimmed_and_cut_to_the_maximum_length()
    {
        var full = new string('m', ToolCall.MaxDeclaredLength);

        Assert.Equal("TASK-42", ToolCall.Declared(" TASK-42 "));
        Assert.Equal(full, ToolCall.Declared(full));
        Assert.Equal(full, ToolCall.Declared(full + "overflow"));
    }
}
