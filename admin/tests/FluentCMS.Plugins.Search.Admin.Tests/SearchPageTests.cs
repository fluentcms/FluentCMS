using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Search.Admin.Tests;

// The page of the half: a rebuild of the index of the site or of one type, whose job a caller who may read jobs is linked
// to, and the refusals of the server named by their code.
public sealed class SearchPageTests : SearchTestContext
{
    [Fact]
    public void OnlyTheTypesWithASearchableFieldAreOfferedAndNoneMeansEveryType()
    {
        var cut = Render<SearchPage>();

        cut.WaitForAssertion(() => Field(cut, "Content type").QuerySelectorAll("option").Select(option => (option.GetAttribute("value"), option.TextContent))
            .ShouldBe([("", "Every searchable type"), ("article", "article"), ("product", "Product page")]));
        cut.Find("h1").TextContent.ShouldBe("Search index");
    }

    [Fact]
    public void ARebuildOfOneTypeIsStartedAndACallerWhoMayReadJobsIsLinkedToItsJob()
    {
        Admin.ForInstance.Add("jobs.manage");
        var cut = Render<SearchPage>();
        cut.WaitForAssertion(() => Field(cut, "Content type").ShouldNotBeNull());

        Field(cut, "Content type").Change("product");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".search-message").TextContent.ShouldBe("The rebuild of Product page has started."));
        var sent = Server.Requests.Single();
        (sent.Method, sent.Path, sent.NoLocale).ShouldBe((HttpMethod.Post, "search/rebuild", true));
        sent.Body!.ToJsonString().ShouldBe("""{"type":"product"}""");
        cut.Find($"a[href='jobs/{RebuildJob:D}']").TextContent.ShouldBe("Follow the job of the rebuild");
    }

    [Fact]
    public void ACallerWhoMayNotReadJobsIsToldThatTheRebuildOfEveryTypeStarted()
    {
        var cut = Render<SearchPage>();
        cut.WaitForAssertion(() => Field(cut, "Content type").ShouldNotBeNull());

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".search-message").TextContent.ShouldStartWith("The rebuild of every searchable type has started. It runs in the background"));
        Server.Requests.Single().Body!.ToJsonString().ShouldBe("""{"type":null}""");
        cut.FindAll("a[href^='jobs/']").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(403, "FCMS2001", "Not started: you may not rebuild the index of this site.")]
    [InlineData(400, "FCMS1700", "Not started: article has no searchable field, so it has nothing to index.")]
    [InlineData(422, "FCMS1701", "Not started: article is no longer a content type.")]
    [InlineData(503, "", "Not started: the server did not answer. Try again.")]
    public void ARefusedRebuildSaysWhy(int status, string code, string message)
    {
        Admin.ForInstance.Add("jobs.manage");
        Server.Refuse = _ => Problem(status, code);
        var cut = Render<SearchPage>();
        cut.WaitForAssertion(() => Field(cut, "Content type").ShouldNotBeNull());

        Field(cut, "Content type").Change("article");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".search-message").TextContent.ShouldBe(message));
        cut.FindAll("a[href^='jobs/']").ShouldBeEmpty();
    }

    [Fact]
    public void WithoutTheTypesEveryTypeIsStillRebuilt()
    {
        Schema.Fails = true;
        var cut = Render<SearchPage>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe("The content types could not be read; the rebuild covers every type."));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".search-message").TextContent.ShouldStartWith("The rebuild of every searchable type has started."));
    }
}
