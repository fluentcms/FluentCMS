using FluentCMS.Content;
using FluentCMS.Publishing;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Content;

public class OriginTests
{
    [Fact]
    public void HasTheSixOrigins()
    {
        Enum.GetNames<Origin>().ShouldBe(["Admin", "Api", "Mcp", "Job", "Cli", "Seed"]);
    }

    [Fact]
    public void ValuesAreStableBecauseTheyArePersisted()
    {
        ((int)Origin.Admin).ShouldBe(1);
        ((int)Origin.Api).ShouldBe(2);
        ((int)Origin.Mcp).ShouldBe(3);
        ((int)Origin.Job).ShouldBe(4);
        ((int)Origin.Cli).ShouldBe(5);
        ((int)Origin.Seed).ShouldBe(6);
    }

    [Fact]
    public void DefaultValueIsNotAnOrigin()
    {
        Enum.IsDefined(default(Origin)).ShouldBeFalse();
    }

    [Fact]
    public void PrincipalKindsAreTheFiveKinds()
    {
        Enum.GetNames<PrincipalKind>().ShouldBe(["AdminUser", "Member", "ApiKey", "ServiceAccount", "System"]);
        Enum.IsDefined(default(PrincipalKind)).ShouldBeFalse();
    }

    [Fact]
    public void EntryStatusIsTheFourStates()
    {
        Enum.GetNames<EntryStatus>().ShouldBe(["Draft", "Published", "Modified", "Unpublished"]);
        default(EntryStatus).ShouldBe(EntryStatus.Draft);
    }
}
