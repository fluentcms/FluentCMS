using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Roles;
using FluentCMS.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration.Roles;

// A grant's identity, by which the merge after FCMS3045 and the duplicate check tell grants apart: its members in a fixed
// order, each list a set normalized as the server stores it, and an object with no member as none. And the refusal of a
// grant carried to the path below it.
public sealed class GrantIdentityTests
{
    private static readonly Guid Role = new("0193a1b2-0000-7000-8000-0000000000c1");

    [Fact]
    public void TheIdentityDoesNotDependOnTheOrderOfTheMembersOrOnWhatTheServerNormalizes()
    {
        var site = "0193A1B2-0000-7000-8000-00000000A001";
        Identity(new JsonObject { ["locales"] = new JsonArray("en"), ["fields"] = new JsonArray("title") })
            .ShouldBe(Identity(new JsonObject { ["fields"] = new JsonArray("title"), ["locales"] = new JsonArray("en") }));
        Identity(new JsonObject { ["locales"] = new JsonArray("FR", "en", "fr") }).ShouldBe(Identity(new JsonObject { ["locales"] = new JsonArray("en", "fr") }));
        Identity(new JsonObject { ["resources"] = new JsonArray("b", "a", "b") }).ShouldBe(Identity(new JsonObject { ["resources"] = new JsonArray("a", "b") }));
        Identity(new JsonObject { ["sites"] = new JsonArray(site) }).ShouldBe(Identity(new JsonObject { ["sites"] = new JsonArray(site.ToLowerInvariant()) }));
        Identity(new JsonObject()).ShouldBe(Identity(null));
        Identity(new JsonObject { ["locales"] = null, ["rowRule"] = null }).ShouldBe(Identity(null));
    }

    [Fact]
    public void GrantsThatReachDifferentlyHaveDifferentIdentities()
    {
        Identity(new JsonObject { ["locales"] = new JsonArray("en") }).ShouldNotBe(Identity(new JsonObject { ["locales"] = new JsonArray("fr") }));
        Identity(new JsonObject { ["fields"] = new JsonArray("Title") }).ShouldNotBe(Identity(new JsonObject { ["fields"] = new JsonArray("title") }));
        Identity(new JsonObject { ["rowRule"] = "own-entries" }).ShouldNotBe(Identity(null));
        Identity(new JsonObject { ["locales"] = new JsonArray() }).ShouldNotBe(Identity(null));
        GrantDraft.IdentityOf("content.article.read", null).ShouldNotBe(GrantDraft.IdentityOf("content.article.update", null));

        // A member the server does not know is kept, since the server refuses it rather than drop it.
        Identity(new JsonObject { ["tenants"] = new JsonArray("a") }).ShouldNotBe(Identity(null));
    }

    // Both changes add the same grant, written with its members in another order: it is there once.
    [Fact]
    public void AGrantBothChangesAddedInAnotherOrderIsThereOnce()
    {
        var read = Read();
        var mine = RoleDraft.From(read);
        mine.Grants.Add(new GrantDraft("content.article.update", new JsonObject { ["locales"] = new JsonArray("en"), ["fields"] = new JsonArray("title") }));
        var current = Read(Grant("content.article.update", new JsonObject { ["fields"] = new JsonArray("title"), ["locales"] = new JsonArray("en") }));

        var (rebased, _) = RoleDraft.Rebase(read, mine, current);

        rebased.Grants.Select(grant => grant.Permission).ShouldBe(["schema.read", "content.article.update"]);
    }

    // The user took away a grant that the other change saved again, as the server answers it: in another order, with its
    // locales normalized. It is the same grant, so it is taken away.
    [Fact]
    public void AGrantTheUserTookAwayIsTakenAwayAsTheServerAnswersIt()
    {
        var read = Read(Grant("content.article.read", new JsonObject { ["locales"] = new JsonArray("en", "fr"), ["fields"] = new JsonArray("title") }));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveAll(grant => grant.Permission == "content.article.read");
        var current = Read(Grant("content.article.read", new JsonObject { ["fields"] = new JsonArray("title"), ["locales"] = new JsonArray("fr", "en") }));

        RoleDraft.Rebase(read, mine, current).Draft.Grants.Select(grant => grant.Permission).ShouldBe(["schema.read"]);
    }

    // The user's grant with empty constraints is the server's grant with none: kept once, not taken away and added again.
    [Fact]
    public void AnEmptyObjectIsTheServersNull()
    {
        var read = Read(Grant("content.article.read", null));
        var mine = RoleDraft.From(read);
        mine.Grants[1].Constraints = [];
        var current = Read(Grant("content.article.read", null), Grant("content.article.create", null));

        RoleDraft.Rebase(read, mine, current).Draft.Grants.Select(grant => grant.Permission).ShouldBe(["schema.read", "content.article.read", "content.article.create"]);
    }

    // The user limited a grant to a field while the other change limited it to a locale: both together would give every field
    // in that locale, and the field in every locale. The other change's grant stays, and the user's change is named.
    [Fact]
    public void AGrantBothChangesNarrowedKeepsTheirsAndNamesMine()
    {
        var read = Read(Grant("content.article.update", null));
        var mine = RoleDraft.From(read);
        mine.Grants[1].Constraints = new JsonObject { ["fields"] = new JsonArray("title") };
        var current = Read(Grant("content.article.update", new JsonObject { ["locales"] = new JsonArray("en") }));

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, current);

        rebased.Grants.Select(grant => grant.Identity).ShouldBe([
            GrantDraft.IdentityOf("schema.read", null),
            GrantDraft.IdentityOf("content.article.update", new JsonObject { ["locales"] = new JsonArray("en") })]);
        dropped.ShouldBe(["content.article.update"]);
    }

    // The user narrowed a grant the other change took away: it stays away.
    [Fact]
    public void AGrantTheOtherChangeTookAwayStaysAwayThoughTheUserNarrowedIt()
    {
        var read = Read(Grant("content.article.update", null));
        var mine = RoleDraft.From(read);
        mine.Grants[1].Constraints = new JsonObject { ["fields"] = new JsonArray("title") };

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, Read());

        rebased.Grants.Select(grant => grant.Permission).ShouldBe(["schema.read"]);
        dropped.ShouldBe(["content.article.update"]);
    }

    // A change of constraints to a grant the other change left as it was read is kept, in its place; one the other change
    // made just as the user did is not named.
    [Fact]
    public void AChangeOfAGrantTheOtherChangeLeftIsKept()
    {
        var read = Read(Grant("content.article.update", null), Grant("content.article.read", null));
        var mine = RoleDraft.From(read);
        mine.Grants[1].Constraints = new JsonObject { ["fields"] = new JsonArray("title") };
        mine.Grants[2].Constraints = new JsonObject { ["locales"] = new JsonArray("fr") };
        var current = Read(Grant("content.article.update", null), Grant("content.article.read", new JsonObject { ["locales"] = new JsonArray("FR") }), Grant("content.article.create", null));

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, current);

        rebased.Grants.Select(grant => grant.Identity).ShouldBe([
            GrantDraft.IdentityOf("schema.read", null),
            GrantDraft.IdentityOf("content.article.update", new JsonObject { ["fields"] = new JsonArray("title") }),
            GrantDraft.IdentityOf("content.article.read", new JsonObject { ["locales"] = new JsonArray("fr") }),
            GrantDraft.IdentityOf("content.article.create", null)]);
        dropped.ShouldBeEmpty();
    }

    // A removal undone adds the grant again as it was read: it is that grant kept, so the other change's narrowing of it stands.
    [Fact]
    public void AGrantRemovedAndAddedAgainKeepsTheOtherChangesNarrowing()
    {
        var read = Read(Grant("content.article.update", null));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveAt(1);
        mine.Grants.Add(new GrantDraft("content.article.update", null));
        var narrowed = new JsonObject { ["sites"] = new JsonArray("0193a1b2-0000-7000-8000-00000000a001") };

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, Read(Grant("content.article.update", narrowed)));

        rebased.Grants.Select(grant => grant.Identity).ShouldBe([GrantDraft.IdentityOf("schema.read", null), GrantDraft.IdentityOf("content.article.update", narrowed)]);
        dropped.ShouldBeEmpty();
    }

    // And when the other change took that grant away, it stays away.
    [Fact]
    public void AGrantRemovedAndAddedAgainStaysAwayWhenTheOtherChangeTookItAway()
    {
        var read = Read(Grant("content.article.update", null));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveAt(1);
        mine.Grants.Add(new GrantDraft("content.article.update", null));

        RoleDraft.Rebase(read, mine, Read()).Draft.Grants.Select(grant => grant.Permission).ShouldBe(["schema.read"]);
    }

    // A limited grant removed, its permission added again and narrowed back to the same limits is the grant read.
    [Fact]
    public void AGrantRemovedAndNarrowedBackToTheSameLimitsKeepsTheOtherChange()
    {
        var read = Read(Grant("content.article.update", new JsonObject { ["locales"] = new JsonArray("fr") }));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveAt(1);
        mine.Grants.Add(new GrantDraft("content.article.update", new JsonObject { ["locales"] = new JsonArray("FR") }));
        var theirs = new JsonObject { ["locales"] = new JsonArray("fr"), ["fields"] = new JsonArray("title") };

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, Read(Grant("content.article.update", theirs)));

        rebased.Grants.Select(grant => grant.Identity).ShouldBe([GrantDraft.IdentityOf("schema.read", null), GrantDraft.IdentityOf("content.article.update", theirs)]);
        dropped.ShouldBeEmpty();
    }

    // A grant removed and added again with other limits is a change of it, as a change in place is: where the other change
    // narrowed it or took it away, the other change's version stands and the user's is named.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGrantRemovedAndAddedAgainWithOtherLimitsIsAChangeOfIt(bool narrowed)
    {
        var read = Read(Grant("content.article.update", null));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveAt(1);
        mine.Grants.Add(new GrantDraft("content.article.update", new JsonObject { ["locales"] = new JsonArray("en") }));
        var theirs = new JsonObject { ["sites"] = new JsonArray("0193a1b2-0000-7000-8000-00000000a001") };
        var current = narrowed ? Read(Grant("content.article.update", theirs)) : Read();

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, current);

        string[] expected = narrowed
            ? [GrantDraft.IdentityOf("schema.read", null), GrantDraft.IdentityOf("content.article.update", theirs)]
            : [GrantDraft.IdentityOf("schema.read", null)];
        rebased.Grants.Select(grant => grant.Identity).ShouldBe(expected);
        dropped.ShouldBe(["content.article.update"]);
    }

    // Where the other change left the grant as read, the user's version takes its place.
    [Fact]
    public void AGrantRemovedAndAddedAgainWithOtherLimitsReplacesTheOneTheOtherChangeLeft()
    {
        var read = Read(Grant("content.article.update", null), Grant("content.article.read", null));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveAt(1);
        mine.Grants.Add(new GrantDraft("content.article.update", new JsonObject { ["locales"] = new JsonArray("en") }));

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, Read(Grant("content.article.update", null), Grant("content.article.read", null), Grant("content.article.create", null)));

        rebased.Grants.Select(grant => grant.Identity).ShouldBe([
            GrantDraft.IdentityOf("schema.read", null),
            GrantDraft.IdentityOf("content.article.update", new JsonObject { ["locales"] = new JsonArray("en") }),
            GrantDraft.IdentityOf("content.article.read", null),
            GrantDraft.IdentityOf("content.article.create", null)]);
        dropped.ShouldBeEmpty();
    }

    // So is a limited grant removed and added again without limits: it widens the grant read, so it holds only where the
    // other change left that grant.
    [Fact]
    public void ALimitedGrantRemovedAndAddedAgainWithoutLimitsIsAChangeOfIt()
    {
        var limited = new JsonObject { ["sites"] = new JsonArray("0193a1b2-0000-7000-8000-00000000a001") };
        var read = Read(Grant("content.article.update", limited));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveAt(1);
        mine.Grants.Add(new GrantDraft("content.article.update", null));
        var theirs = new JsonObject { ["sites"] = new JsonArray("0193a1b2-0000-7000-8000-00000000a001"), ["locales"] = new JsonArray("fr") };

        var (kept, keptDropped) = RoleDraft.Rebase(read, mine, Read(Grant("content.article.update", limited)));
        var (narrowed, narrowedDropped) = RoleDraft.Rebase(read, mine, Read(Grant("content.article.update", theirs)));

        kept.Grants.Select(grant => grant.Identity).ShouldBe([GrantDraft.IdentityOf("schema.read", null), GrantDraft.IdentityOf("content.article.update", null)]);
        keptDropped.ShouldBeEmpty();
        narrowed.Grants.Select(grant => grant.Identity).ShouldBe([GrantDraft.IdentityOf("schema.read", null), GrantDraft.IdentityOf("content.article.update", theirs)]);
        narrowedDropped.ShouldBe(["content.article.update"]);
    }

    // The user took away both grants of a permission and added one with other limits: which it changes cannot be told, so it
    // holds only where the other change left both as read. Otherwise both stay taken away and the user's grant is named.
    [Theory]
    [InlineData("narrowed")]
    [InlineData("removed")]
    [InlineData("crossed")]
    [InlineData("unchanged")]
    public void AGrantAddedForSeveralTakenAwayHoldsOnlyWhereTheOtherChangeLeftThemAll(string other)
    {
        var first = new JsonObject { ["sites"] = new JsonArray("0193a1b2-0000-7000-8000-00000000a001") };
        var second = new JsonObject { ["sites"] = new JsonArray("0193a1b2-0000-7000-8000-00000000a002") };
        JsonObject In(JsonObject sites, string locale) => new() { ["sites"] = sites["sites"]!.DeepClone(), ["locales"] = new JsonArray(locale) };
        var read = Read(Grant("content.article.update", first), Grant("content.article.update", second));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveRange(1, 2);
        mine.Grants.Add(new GrantDraft("content.article.update", In(second, "en")));
        if (other == "crossed")
        {
            mine.Grants.Add(new GrantDraft("content.article.update", In(first, "en")));
        }

        var current = other switch
        {
            "narrowed" => Read(Grant("content.article.update", first), Grant("content.article.update", In(second, "fr"))),
            "removed" => Read(Grant("content.article.update", first)),
            "crossed" => Read(Grant("content.article.update", In(first, "fr")), Grant("content.article.update", second)),
            _ => read,
        };

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, current);

        string[] expected = other switch
        {
            "narrowed" => [GrantDraft.IdentityOf("content.article.update", In(second, "fr"))],
            "removed" => [],
            "crossed" => [GrantDraft.IdentityOf("content.article.update", In(first, "fr"))],
            _ => [GrantDraft.IdentityOf("content.article.update", In(second, "en"))],
        };
        rebased.Grants.Skip(1).Select(grant => grant.Identity).ShouldBe(expected);
        string[] named = other == "unchanged" ? [] : ["content.article.update"];
        dropped.ShouldBe(named);
    }

    // A new grant of a permission whose grants the user kept is added beside them.
    [Fact]
    public void AGrantOfAPermissionTheUserKeptIsAdded()
    {
        var first = new JsonObject { ["sites"] = new JsonArray("0193a1b2-0000-7000-8000-00000000a001") };
        var second = new JsonObject { ["sites"] = new JsonArray("0193a1b2-0000-7000-8000-00000000a002") };
        var read = Read(Grant("content.article.update", first));
        var mine = RoleDraft.From(read);
        mine.Grants.Add(new GrantDraft("content.article.update", second.DeepClone().AsObject()));

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, Read(Grant("content.article.update", first)));

        rebased.Grants.Skip(1).Select(grant => grant.Identity).ShouldBe([
            GrantDraft.IdentityOf("content.article.update", first),
            GrantDraft.IdentityOf("content.article.update", second)]);
        dropped.ShouldBeEmpty();
    }

    // The user took B away and changed A into B; the other change took A away. Neither is left, so the change is named.
    [Fact]
    public void AChangeTheMergeLeavesOutIsNamedThoughTheServerHadItsIdentity()
    {
        var english = new JsonObject { ["locales"] = new JsonArray("en") };
        var french = new JsonObject { ["locales"] = new JsonArray("fr") };
        var read = Read(Grant("content.article.update", english), Grant("content.article.update", french));
        var mine = RoleDraft.From(read);
        mine.Grants.RemoveAt(2);
        mine.Grants[1].Constraints = french.DeepClone().AsObject();

        var (rebased, dropped) = RoleDraft.Rebase(read, mine, Read(Grant("content.article.update", french)));

        rebased.Grants.Select(grant => grant.Permission).ShouldBe(["schema.read"]);
        dropped.ShouldBe(["content.article.update"]);
    }

    [Fact]
    public void ARefusalOfAGrantIsCarriedToThePathBelowIt()
    {
        var refusal = Refusal(
            ("grants[1].constraints.locales", "FCMS2311"),
            ("grants[1].constraints.rowRule", "FCMS3042"),
            ("grants[3]", "FCMS2108"),
            ("grants[0].permission", "FCMS3042"),
            ("name", "FCMS3041"));

        RoleProblems.RefusedGrants(refusal).ShouldBe([(1, "constraints.locales"), (1, "constraints.rowRule"), (3, ""), (0, "permission")]);
        RoleProblems.RefusedGrants(new HttpRequestException()).ShouldBeEmpty();
    }

    private static string Identity(JsonObject? constraints) => GrantDraft.IdentityOf("content.article.read", constraints);

    private static RoleGrantRecord Grant(string permission, JsonObject? constraints) => new() { Permission = permission, Constraints = constraints };

    private static RoleRecord Read(params RoleGrantRecord[] grants) =>
        new() { Id = Role, Name = "Translators", Grants = [Grant("schema.read", null), .. grants] };

    private static ApiProblemException Refusal(params (string Path, string Code)[] errors) =>
        new(new ApiProblem
        {
            Status = (int)HttpStatusCode.BadRequest,
            Code = errors[0].Code,
            Errors = [.. errors.Select(error => new ApiProblemError { Path = error.Path, Code = error.Code })],
        });
}
