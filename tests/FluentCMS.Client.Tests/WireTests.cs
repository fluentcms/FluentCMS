using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Client.Tests.Blog;
using Shouldly;
using Xunit;

namespace FluentCMS.Client.Tests;

// What the client sends and how it reads an answer, against a handler that records the request and answers as given.
public sealed class WireTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private static readonly Guid AuthorId = Guid.Parse("0192f2a4-1111-7000-8000-000000000001");
    private static readonly Guid AssetId = Guid.Parse("0192f2a4-3333-7000-8000-000000000003");

    [Fact]
    public async Task APopulatedAssetALocalizedReadAndAnUnknownBlockAreRead()
    {
        var handler = new Recording(HttpStatusCode.OK, "application/json", $$"""
            {
              "id": "{{Guid.Empty}}", "status": "published", "locale": "fr", "version": 3,
              "title": "Bonjour",
              "hero": { "id": "{{AssetId}}", "type": "media", "fileName": "hero.png", "mimeType": "image/png", "width": 16, "height": 9, "alt": "A banner", "urls": { "original": "https://cdn.example.com/hero.png", "presets": { "thumb": "https://cdn.example.com/hero-thumb.png" } } },
              "sections": [ { "id": "b1", "heading": "Hi", "type": "hero" }, { "type": "carousel", "id": "b2", "slides": 3 } ],
              "fallbacks": { "body": "en" }
            }
            """);
        var client = new FluentCmsClient(new HttpClient(handler) { BaseAddress = new Uri("https://cms.example.com/cms") });

        var article = await client.GetAsync<Article>(Guid.Empty, new ContentQuery { Locale = "fr", Populate = ["hero"] }, Token);

        handler.Request!.RequestUri!.ToString().ShouldBe($"https://cms.example.com/cms/api/delivery/v1/article/{Guid.Empty}?populate=hero&locale=fr");
        article.Hero!.Urls!.Original.ShouldBe("https://cdn.example.com/hero.png");
        article.Hero.Urls.Presets!["thumb"].ShouldBe("https://cdn.example.com/hero-thumb.png");
        article.Hero.Alt.ShouldBe("A banner");
        article.Hero.Width.ShouldBe(16);
        article.Fallbacks!.Body.ShouldBe("en");
        article.Fallbacks.Title.ShouldBeNull();
        article.Sections![0].ShouldBeOfType<HeroBlock>().Heading.ShouldBe("Hi");
        article.Sections[1].GetType().ShouldBe(typeof(Block));
        article.Sections[1].Id.ShouldBe("b2");
    }

    [Fact]
    public async Task AWriteSendsIdsForRelationsAndMediaTheBlockTypesAndNoSystemField()
    {
        var handler = new Recording(HttpStatusCode.OK, "application/json", $$"""{ "id": "{{Guid.Empty}}", "version": 8, "scheduledPublishAt": null, "scheduledUnpublishAt": "2027-03-01T09:00:00Z" }""");
        var client = new FluentCmsClient(new HttpClient(handler) { BaseAddress = new Uri("https://cms.example.com/") });
        var article = new Article
        {
            Id = Guid.NewGuid(),
            Version = 7,
            Status = "draft",
            ScheduledPublishAt = DateTimeOffset.UnixEpoch,
            Title = "Hello",
            Author = new Relation<Author>(AuthorId) { Type = "author", Entry = new Author { Name = "Ada" } },
            Hero = new MediaReference(AssetId) { Type = "media", FileName = "hero.png" },
            Sections = [new QuoteBlock { Text = "Typed." }],
            Fallbacks = new ArticleFallbacks { Body = "en" },
        };

        var updated = await client.UpdateAsync(Guid.Empty, article, 7, new WriteOptions("fr"), Token);

        updated.Version.ShouldBe(8);
        updated.ScheduledPublishAt.ShouldBeNull();
        updated.ScheduledUnpublishAt.ShouldBe(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

        handler.Request!.Method.ShouldBe(HttpMethod.Put);
        handler.Request.RequestUri!.ToString().ShouldBe($"https://cms.example.com/api/management/v1/content/article/{Guid.Empty}?locale=fr");
        handler.Request.Headers.IfMatch.Single().Tag.ShouldBe("\"7\"");
        var body = JsonNode.Parse(handler.Body!)!.AsObject();
        body.Select(property => property.Key).ShouldBe(["title", "author", "hero", "sections"], ignoreOrder: true);
        ((string?)body["author"]).ShouldBe(AuthorId.ToString());
        ((string?)body["hero"]).ShouldBe(AssetId.ToString());
        body["sections"]!.ToJsonString().ShouldBe("""[{"type":"quote","text":"Typed."}]""");
    }

    [Fact]
    public async Task AnAnswerThatIsNotAProblemHasNoCode()
    {
        var handler = new Recording(HttpStatusCode.BadGateway, "text/html", "<html>gateway</html>");
        var client = new FluentCmsClient(new HttpClient(handler) { BaseAddress = new Uri("https://cms.example.com/") });

        var problem = await Should.ThrowAsync<FluentCmsProblemException>(() => client.ListAsync<Article>(cancellationToken: Token));

        problem.Status.ShouldBe(HttpStatusCode.BadGateway);
        problem.Code.ShouldBeNull();
        problem.Message.ShouldStartWith("502: ");
    }

    private sealed class Recording(HttpStatusCode status, string mediaType, string answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, mediaType) };
        }
    }
}
