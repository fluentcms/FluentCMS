using Bunit;
using FluentCMS.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Webhooks.Admin.Tests;

// The sweep of spec 7: every page of the half under both pseudo-cultures, the webhooks and their deliveries marked as data.
public sealed class LocalizationTests : WebhooksTestContext
{
    public static TheoryData<string, string> Pages() => PseudoSweep.Pages(typeof(WebhooksPage).Assembly);

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTextOfEveryPageComesFromTheResources(string culture, string page)
    {
        this.UsePseudoCulture(culture);
        string Data(string text) => PseudoCulture.Transform(text, culture);
        Services.Replace(ServiceDescriptor.Singleton<IAdminSchema>(new FakeSchema(Data)));
        var id = Server.Add(Data("Receiver"), Data("https://hooks.example.com/in"), [Data("content.published")]);
        Server.AddDelivery(id, Data("content.published"), "dead", 500, response: Data("Receiver broke"));

        IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut = page switch
        {
            nameof(WebhooksPage) => Render<WebhooksPage>(),
            nameof(WebhookPage) => Render<WebhookPage>(parameters => parameters.Add(page => page.Id, id)),
            _ => throw PseudoSweep.NoSweep(page),
        };

        cut.WaitForAssertion(() => cut.FindAll(page == nameof(WebhooksPage) ? "table tbody tr" : "tr.webhooks-attempt").Count.ShouldBe(1));
        cut.ShouldBeLocalized();
    }
}
