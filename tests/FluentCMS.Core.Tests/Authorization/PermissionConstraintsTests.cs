using System.Text.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

public class PermissionConstraintsTests
{
    private static readonly Guid Site = Guid.Parse("0192e0a0-0000-7000-8000-0000000000a1");

    [Fact]
    public void NoConstraintsIsStoredAsNull()
    {
        PermissionConstraints.None.IsEmpty.ShouldBeTrue();
        PermissionConstraints.None.ToJson().ShouldBeNull();
        PermissionConstraints.FromJson(null).IsEmpty.ShouldBeTrue();
        PermissionConstraints.FromJson("  ").IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void RoundTripsEveryMember()
    {
        var constraints = new PermissionConstraints
        {
            Sites = [new SiteId(Site)],
            Locales = ["en", "fr"],
            Fields = ["title", "slug"],
            RowRule = "own-entries",
            Resources = ["folder-a"],
        };

        var json = constraints.ToJson();
        var back = PermissionConstraints.FromJson(json);

        json.ShouldBe($$"""{"sites":["{{Site}}"],"locales":["en","fr"],"fields":["title","slug"],"rowRule":"own-entries","resources":["folder-a"]}""");
        back.Sites.ShouldBe([new SiteId(Site)]);
        back.Locales.ShouldBe(["en", "fr"]);
        back.Fields.ShouldBe(["title", "slug"]);
        back.RowRule.ShouldBe("own-entries");
        back.Resources.ShouldBe(["folder-a"]);
        new PermissionConstraints { Resources = ["folder-a"] }.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void ANullMemberDoesNotNarrowAndAnEmptyListDoes()
    {
        var back = PermissionConstraints.FromJson("""{"fields":[]}""");

        back.Fields.ShouldNotBeNull().ShouldBeEmpty();
        back.Sites.ShouldBeNull();
        back.Locales.ShouldBeNull();
        back.RowRule.ShouldBeNull();
    }

    [Fact]
    public void AnUnknownConstraintFailsInsteadOfWideningTheGrant() =>
        Should.Throw<JsonException>(() => PermissionConstraints.FromJson("""{"region":"eu"}"""));
}
