using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// The seeded Blog sample behind a TestServer. The default site's Public role reads articles and tags, and authors
// without their bio; the homepage singleton and products are for keys only. Two more sites have no content.
public sealed class DeliveryFixture : IAsyncLifetime
{
    public const string Headline = "Welcome home";
    public const string ProductType = "gadget";

    public FluentCmsTestHost Host { get; } = new FluentCmsTestHost()
        .WithContentTypes([.. BlogSample.ContentTypes, typeof(Homepage), typeof(Product)])
        .WithServices(TestScheme.Add)
        .Configure(cms => cms.AddBlogSample())
        .Seed(async services =>
        {
            var homepages = services.GetRequiredService<IContentService<Homepage>>();
            var homepage = await homepages.CreateAsync(new Homepage { Headline = Headline });
            await homepages.PublishAsync(homepage.Id);

            var products = services.GetRequiredService<IContentService<Product>>();
            var product = await products.CreateAsync(new Product { Type = ProductType });
            await products.PublishAsync(product.Id);
        });

    public HttpClient Client { get; private set; } = null!;

    public Guid OtherSite { get; private set; }

    public Guid ThirdSite { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().AddRange(
                Grant("content.article.read"),
                Grant("content.tag.read"),
                Grant("content.author.read", new PermissionConstraints { Fields = ["name", "slug"] }));
            await context.SaveChangesAsync(token);

            var sites = scope.ServiceProvider.GetRequiredService<SiteManager>();
            OtherSite = (await sites.CreateAsync("Other", "other", "en", token)).Id;
            ThirdSite = (await sites.CreateAsync("Third", "third", "en", token)).Id;
        }

        Client = Host.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
    }

    public async Task<(HttpStatusCode Status, JsonNode Body)> GetAsync(string path, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/delivery/v1/" + path, UriKind.Relative));
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        using var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, JsonNode.Parse(text)!);
    }

    // A key of the instance-wide Editor role, which reads every type in every site, optionally bound to sites.
    public static (string, string)[] EditorKey(params Guid[] sites)
    {
        List<(string, string)> headers =
        [
            (TestScheme.KindHeader, nameof(PrincipalKind.ApiKey)),
            (TestScheme.RolesHeader, BuiltInRoles.Editor.ToString()),
            (TestScheme.ScopeHeader, TokenScopes.Delivery),
        ];
        if (sites.Length > 0)
        {
            headers.Add((TestScheme.SitesHeader, string.Join(' ', sites)));
        }

        return [.. headers];
    }

    private static RoleGrant Grant(string permission, PermissionConstraints? constraints = null) => new()
    {
        Id = Guid.CreateVersion7(),
        RoleId = DefaultSite.PublicRole,
        Permission = permission,
        Constraints = constraints?.ToJson(),
    };
}
