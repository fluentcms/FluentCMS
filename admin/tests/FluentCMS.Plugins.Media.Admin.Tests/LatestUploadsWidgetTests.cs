using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bunit;
using FluentCMS.Admin.Api;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

// The widget of the latest uploads against a Management API that answers what the test sets.
public sealed class LatestUploadsWidgetTests : BunitContext
{
    private readonly FakeApi api = new();

    public LatestUploadsWidgetTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLocalization();
        Services.AddSingleton<IManagementApi>(api);
    }

    [Fact]
    public void ItListsTheNewestAssetsByTitleElseFileNameAfterOneReadOfFive()
    {
        api.Answer = """
            { "items": [
                { "id": "0193a1b2-0000-7000-8000-000000000001", "fileName": "harbour.jpg", "title": "The harbour", "mimeType": "image/jpeg" },
                { "id": "0193a1b2-0000-7000-8000-000000000002", "fileName": "notes.pdf", "title": null },
                { "id": "0193a1b2-0000-7000-8000-000000000003", "fileName": "logo.svg", "title": " " }
            ], "total": 3, "page": 1, "pageSize": 5 }
            """;

        var widget = Render<LatestUploadsWidget>();

        widget.WaitForAssertion(() => widget.FindAll("li").Select(item => item.TextContent).ShouldBe(["The harbour", "notes.pdf", "logo.svg"]));
        widget.Find("h2").TextContent.ShouldBe("Latest uploads");
        widget.Find("a[href='media']").TextContent.ShouldBe("Open the media library");
        api.Requests.ShouldBe([(HttpMethod.Get, "media?pageSize=5")]);
    }

    [Fact]
    public void WithNoAssetItSaysNothingHasBeenUploaded()
    {
        api.Answer = """{ "items": [], "total": 0, "page": 1, "pageSize": 5 }""";

        var widget = Render<LatestUploadsWidget>();

        widget.WaitForAssertion(() => widget.Find("p").TextContent.ShouldBe("Nothing has been uploaded yet."));
        widget.FindAll("li").ShouldBeEmpty();
    }

    [Fact]
    public void WhileItReadsItSaysSoAndARefusedReadIsAnAlert()
    {
        api.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Refuse = true;

        var widget = Render<LatestUploadsWidget>();
        widget.Find("p[role=status]").TextContent.ShouldBe("Loading the latest uploads…");
        api.Hold.SetResult();

        widget.WaitForAssertion(() => widget.Find("p[role=alert]").TextContent.ShouldBe("The latest uploads could not be read."));
        widget.FindAll("p[role=status]").ShouldBeEmpty();
    }

    private sealed class FakeApi : IManagementApi
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        public string Answer { get; set; } = "{}";

        public bool Refuse { get; set; }

        public TaskCompletionSource? Hold { get; set; }

        public async Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.Path));
            await (Hold?.Task ?? Task.CompletedTask);
            if (Refuse)
            {
                throw new ApiProblemException(new ApiProblem { Status = 403, Title = "Forbidden" });
            }

            return new ApiResponse<T>(HttpStatusCode.OK, null, JsonSerializer.Deserialize(Answer, answer)!);
        }

        public Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
