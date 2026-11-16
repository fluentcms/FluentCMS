using System.Net;
using System.Text;

namespace FluentCMS.Admin.Tests.Configuration;

// Answers config.json with the given body and status, and every other request with 404, and records each request it sees.
internal sealed class FakeHandler(string? configJson, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public static readonly Uri AppBase = new("https://admin.example.com/admin/");

    public List<HttpRequestMessage> Requests { get; } = [];

    public string ContentType { get; init; } = "application/json; charset=utf-8";

    public HttpClient CreateClient() => new(this) { BaseAddress = AppBase };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.RequestUri == new Uri(AppBase, "config.json") && configJson is not null)
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(configJson));
            content.Headers.TryAddWithoutValidation("Content-Type", ContentType);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
