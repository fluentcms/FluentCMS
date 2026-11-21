using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.MultiSite.Admin.Tests;

public sealed class MultiSiteHostsPageTests : MultiSiteTestContext
{
    [Fact]
    public void AnAdministratorAddsASecondHostNameMakesItPrimaryAndRemovesTheFirst()
    {
        Server.AddHost(Shop, "shop.example.com");
        var cut = Render<MultiSiteHostsPage>(parameters => parameters.Add(page => page.Id, Shop));
        cut.WaitForAssertion(() => cut.Find("h1").TextContent.ShouldBe("Host names of Shop"));
        Hosts(cut).ShouldBe(["shop.example.com (primary) Remove shop.example.com"]);

        Field(cut, "Host name").Input("www.shop.example");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Hosts(cut).Length.ShouldBe(2));
        Server.Requests.Last(request => request.Method == HttpMethod.Post).Body!.ToJsonString().ShouldBe("""{"hostname":"www.shop.example","isPrimary":false}""");

        // The primary host is removed last, so it has no remove while another host is there.
        Hosts(cut).ShouldBe(["shop.example.com (primary)", "www.shop.example Make primary www.shop.example Remove www.shop.example"]);

        Button(cut, "Make primary www.shop.example").Click();
        cut.WaitForAssertion(() => Hosts(cut)[0].ShouldBe("www.shop.example (primary)"));
        Server.Requests.Last(request => request.Method == HttpMethod.Patch).Body!.ToJsonString().ShouldBe("""{"isPrimary":true}""");
        cut.Find(".multi-site-message").TextContent.ShouldBe("www.shop.example is now the primary host.");

        Button(cut, "Remove shop.example.com").Click();
        cut.WaitForAssertion(() => Hosts(cut).ShouldBe(["www.shop.example (primary) Remove www.shop.example"]));
        Server.Requests.ShouldContain(request => request.Method == HttpMethod.Delete && request.Path == $"sites/{Shop:D}/hosts/shop.example.com");
    }

    [Fact]
    public void AHostNameTheServerRefusesShowsTheReasonByTheField()
    {
        Server.Refuse = request => request.Method == HttpMethod.Post ? Problem(422, "FCMS1662", "hostname") : null;
        var cut = Render<MultiSiteHostsPage>(parameters => parameters.Add(page => page.Id, Shop));
        cut.WaitForAssertion(() => cut.Find("h1").TextContent.ShouldBe("Host names of Shop"));

        Field(cut, "Host name").Input("shop.example.com:8080");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("A host name has no port; a site answers on every port of its host names."));
        Field(cut, "Host name").GetAttribute("aria-invalid").ShouldBe("true");
        Server.Hosts[Shop].ShouldBeEmpty();
    }

    [Fact]
    public void ASiteThatDoesNotExistSaysSo()
    {
        var cut = Render<MultiSiteHostsPage>(parameters => parameters.Add(page => page.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.ShouldBe("The site was not found"));
        cut.FindAll("form").ShouldBeEmpty();
    }

    // Each host with its mark and the accessible names of its buttons.
    private static string[] Hosts(IRenderedComponent<MultiSiteHostsPage> cut) =>
        [.. cut.FindAll("ul.multi-site-hosts li").Select(item => string.Join(' ', item.Children.Select(child => child.TextContent.Trim()).Where(text => text.Length > 0)))];
}
