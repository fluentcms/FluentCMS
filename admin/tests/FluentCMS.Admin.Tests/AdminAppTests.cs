using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class AdminAppTests : BunitContext
{
    [Fact]
    public void TheAppRendersNothingUntilTheShellIsThere()
    {
        var app = Render<AdminApp>();

        app.Markup.ShouldBeEmpty();
    }
}
