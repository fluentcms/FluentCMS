using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The plugin names ai.use for the built-in Editor and Author by default, which a new instance gives them at its first
// start (#3103).
public sealed class AiBuiltInRoleTests : IClassFixture<AiHost>
{
    private readonly AiHost _ai;

    public AiBuiltInRoleTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Theory]
    [InlineData(BuiltInRoles.EditorName)]
    [InlineData(BuiltInRoles.AuthorName)]
    public async Task TheBuiltInEditorAndAuthorRunAnAction(string role)
    {
        var site = await _ai.SiteAsync();
        using var client = await _ai.RoleAsync(site, role);
        _ai.Chat.Answer = "Ships are large vessels.";

        var answer = await SuggestAsync(client, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });

        answer["suggestion"]!.GetValue<string>().ShouldBe("Ships are large vessels.");
    }
}
