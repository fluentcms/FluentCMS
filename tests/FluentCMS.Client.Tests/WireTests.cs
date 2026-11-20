using System.Net;
using System.Net.Http.Headers;
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

    // A success the client cannot read is a problem with the status, no code and the reason, and not an exception of the
    // JSON reader, a delete's too. A delete returns nothing, so an empty answer to it is no problem.
    [Fact]
    public async Task ASuccessThatIsNotJsonIsAProblemAndAnEmptyDeleteIsNot()
    {
        var proxy = new FluentCmsClient(new HttpClient(new Recording(HttpStatusCode.OK, "text/html", "<html>a proxy</html>")) { BaseAddress = new Uri("https://cms.example.com/") });
        var chunked = new FluentCmsClient(new HttpClient(new Recording(HttpStatusCode.OK, "application/json", "", chunked: true)) { BaseAddress = new Uri("https://cms.example.com/") });

        var problem = await Should.ThrowAsync<FluentCmsProblemException>(() => proxy.GetAsync<Article>(Guid.Empty, cancellationToken: Token));
        (await Should.ThrowAsync<FluentCmsProblemException>(() => proxy.DeleteAsync<Article>(Guid.Empty, Token))).Detail.ShouldBe("The answer is not JSON.");
        await chunked.DeleteAsync<Article>(Guid.Empty, Token);

        problem.Status.ShouldBe(HttpStatusCode.OK);
        problem.Code.ShouldBeNull();
        problem.Detail.ShouldBe("The answer is not JSON.");
        problem.Message.ShouldBe("200: The answer is not JSON.");
        problem.InnerException.ShouldBeAssignableTo<System.Text.Json.JsonException>();
    }

    // Every call but a delete returns a result, so an empty answer, also one that ends at once without saying its length,
    // a 204 or a literal null is a problem with no code, and not a null the caller would read later.
    [Theory]
    [InlineData(HttpStatusCode.OK, "", false, "The answer is empty.")]
    [InlineData(HttpStatusCode.OK, "", true, "The answer is empty.")]
    [InlineData(HttpStatusCode.NoContent, "", false, "The answer is empty.")]
    [InlineData(HttpStatusCode.OK, "null", false, "The answer is null.")]
    public async Task ASuccessWithNoResultIsAProblemWhereTheCallReturnsOne(HttpStatusCode status, string answer, bool chunked, string detail)
    {
        var client = new FluentCmsClient(new HttpClient(new Recording(status, "application/json", answer, chunked)) { BaseAddress = new Uri("https://cms.example.com/") });
        var article = new Article { Title = "Hello" };

        Func<Task>[] calls =
        [
            () => client.ListAsync<Article>(cancellationToken: Token),
            () => client.GetAsync<Article>(Guid.Empty, cancellationToken: Token),
            () => client.GetByAsync<Article>("slug", "hello", cancellationToken: Token),
            () => client.GetSingletonAsync<Article>(cancellationToken: Token),
            () => client.CreateAsync(article, cancellationToken: Token),
            () => client.UpdateAsync(Guid.Empty, article, 1, cancellationToken: Token),
            () => client.PublishAsync<Article>(Guid.Empty, cancellationToken: Token),
            () => client.UnpublishAsync<Article>(Guid.Empty, cancellationToken: Token),
        ];
        foreach (var call in calls)
        {
            var problem = await Should.ThrowAsync<FluentCmsProblemException>(call);
            problem.Status.ShouldBe(status);
            problem.Code.ShouldBeNull();
            problem.Detail.ShouldBe(detail);
        }

        await client.DeleteAsync<Article>(Guid.Empty, Token);
    }

    // . and .. would be removed as the address is resolved and reach another route with the client's key.
    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public async Task ADotSegmentIsRefusedInEveryPartOfThePath(string segment)
    {
        var handler = new Recording(HttpStatusCode.OK, "application/json", "{}");
        var client = new FluentCmsClient(new HttpClient(handler) { BaseAddress = new Uri("https://cms.example.com/") });

        Func<Task>[] calls =
        [
            () => client.ListAsync<Article>(segment, cancellationToken: Token),
            () => client.GetAsync<Article>(segment, Guid.Empty, cancellationToken: Token),
            () => client.GetByAsync<Article>(segment, "slug", "hello", cancellationToken: Token),
            () => client.GetByAsync<Article>("article", segment, "hello", cancellationToken: Token),
            () => client.GetByAsync<Article>("article", "slug", segment, cancellationToken: Token),
            () => client.GetSingletonAsync<Article>(segment, cancellationToken: Token),
            () => client.CreateAsync(segment, new Article(), cancellationToken: Token),
            () => client.UpdateAsync(segment, Guid.Empty, new Article(), 1, cancellationToken: Token),
            () => client.DeleteAsync(segment, Guid.Empty, Token),
            () => client.PublishAsync<Article>(segment, Guid.Empty, cancellationToken: Token),
            () => client.UnpublishAsync<Article>(segment, Guid.Empty, cancellationToken: Token),
        ];
        foreach (var call in calls)
        {
            await Should.ThrowAsync<ArgumentException>(call);
        }

        handler.Request.ShouldBeNull();
    }

    // Any other value is escaped, its % too, so an escaped dot reaches the server as the text it is.
    [Theory]
    [InlineData("%2e%2e", "%252e%252e")]
    [InlineData("%2E", "%252E")]
    [InlineData("...", "...")]
    [InlineData("a/../b", "a%2F..%2Fb")]
    public async Task AnyOtherSegmentIsSentEscaped(string value, string sent)
    {
        var handler = new Recording(HttpStatusCode.OK, "application/json", "{}");
        var client = new FluentCmsClient(new HttpClient(handler) { BaseAddress = new Uri("https://cms.example.com/") });

        await client.GetByAsync<Article>("slug", value, cancellationToken: Token);

        handler.Request!.RequestUri!.AbsoluteUri.ShouldBe($"https://cms.example.com/api/delivery/v1/article/by/slug/{sent}");
    }

    // Seconds as the API sends them, or a date read against the Date of the same answer.
    [Fact]
    public async Task AProblemHasTheRetryAfterOfTheAnswer()
    {
        var noon = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var seconds = new Recording(HttpStatusCode.TooManyRequests, "application/problem+json", """{ "code": "FCMS3101" }""", retryAfter: new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));
        var date = new Recording(HttpStatusCode.ServiceUnavailable, "text/plain", "busy", retryAfter: new RetryConditionHeaderValue(noon.AddMinutes(1)), date: noon);
        var past = new Recording(HttpStatusCode.ServiceUnavailable, "text/plain", "busy", retryAfter: new RetryConditionHeaderValue(noon.AddMinutes(-1)), date: noon);
        var undated = new Recording(HttpStatusCode.ServiceUnavailable, "text/plain", "busy", retryAfter: new RetryConditionHeaderValue(noon.AddMinutes(1)));
        var none = new Recording(HttpStatusCode.TooManyRequests, "text/plain", "slow down");

        var limited = await Problem(seconds);
        limited.RetryAfter.ShouldBe(TimeSpan.FromSeconds(30));
        limited.Code.ShouldBe("FCMS3101");
        (await Problem(date)).RetryAfter.ShouldBe(TimeSpan.FromMinutes(1));
        (await Problem(past)).RetryAfter.ShouldBe(TimeSpan.Zero);
        (await Problem(undated)).RetryAfter.ShouldBeNull();
        (await Problem(none)).RetryAfter.ShouldBeNull();
        foreach (var malformed in new[] { "-5", "1.5", "+30", "30s", "soon" })
        {
            (await Problem(new Recording(HttpStatusCode.TooManyRequests, "text/plain", "slow down", date: noon, rawRetryAfter: malformed))).RetryAfter.ShouldBeNull(malformed);
        }

        static Task<FluentCmsProblemException> Problem(Recording handler) =>
            Should.ThrowAsync<FluentCmsProblemException>(() => new FluentCmsClient(new HttpClient(handler) { BaseAddress = new Uri("https://cms.example.com/") }).ListAsync<Article>(cancellationToken: Token));
    }

    private sealed class Recording(HttpStatusCode status, string mediaType, string answer, bool chunked = false, RetryConditionHeaderValue? retryAfter = null, DateTimeOffset? date = null, string? rawRetryAfter = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            HttpContent content = chunked ? new StreamContent(new Unseekable(Encoding.UTF8.GetBytes(answer))) : new StringContent(answer, Encoding.UTF8, mediaType);
            content.Headers.ContentType ??= new MediaTypeHeaderValue(mediaType);
            var response = new HttpResponseMessage(status) { Content = content };
            response.Headers.RetryAfter = retryAfter;
            response.Headers.Date = date;
            if (rawRetryAfter is not null)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", rawRetryAfter);
            }

            return response;
        }
    }

    // A body whose length is not known, as one sent in chunks.
    private sealed class Unseekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
