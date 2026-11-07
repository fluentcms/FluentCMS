using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Hooks;
using FluentCMS.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public sealed class ProblemDetailsTests : IAsyncLifetime
{
    private const string SignedIn = "X-Signed-In";

    private static readonly Dictionary<string, Func<Exception>> Throwers = new()
    {
        ["denied"] = () => new ContentAccessDeniedException("article", "update"),
        ["settings-denied"] = () => new SettingsAccessDeniedException("seo", null),
        ["not-found"] = () => new EntryNotFoundException("article", Guid.Parse("11111111-1111-1111-1111-111111111111")),
        ["conflict"] = () => new VersionConflictException(2, 3, new ContentDocument { ["title"] = "Stored", ["views"] = 7 }),
        ["conflict-hidden"] = () => new VersionConflictException(2, 3, null),
        ["required"] = () => new ValidationException([new ValidationError("title", ValidationErrorCodes.Required, "A value is required.")]),
        ["several"] = () => new ValidationException(
        [
            new ValidationError("title", ValidationErrorCodes.Required, "A value is required."),
            new ValidationError("seo.metaTitle", ValidationErrorCodes.MaxLength, "Must have at most 60 characters, has 80."),
            new ValidationError("sections[2].heading", ValidationErrorCodes.Required, "A value is required."),
        ]),
        ["query"] = () => new ValidationException([new ValidationError("filters[nope]", "FCMS2202", "There is no field nope.")]),
        ["query-several"] = () => new ValidationException(
        [
            new ValidationError("filters[a]", "FCMS2202", "There is no field a."),
            new ValidationError("filters[b]", "FCMS2204", "Not a number."),
        ]),
        ["mixed"] = () => new ValidationException(
        [
            new ValidationError("filters[a]", "FCMS2202", "There is no field a."),
            new ValidationError("title", ValidationErrorCodes.Required, "A value is required."),
        ]),
        ["singleton"] = () => new ValidationException([new ValidationError("", "FCMS2401", "Home is a singleton and already has its entry.")]),
        ["site-missing"] = () => new ValidationException([new ValidationError("site", "FCMS2301", "There is no site.")]),
        ["settings-conflict"] = () => new ValidationException([new ValidationError("", "FCMS2808", "The settings area changed since it was read.")]),
        ["hook-plugin"] = () => new HookRejectedException("ACME0001", "Titles may not contain the word free."),
        ["hook-engine"] = () => new HookRejectedException("FCMS2405", "A hook changed the draft being published."),
        ["rich-text"] = () => new RichTextValidationException([new RichTextValidationError("content[0]", "Unknown node.")]),
        ["bad-request"] = () => new BadHttpRequestException("The JSON is not valid.", StatusCodes.Status400BadRequest),
        ["media-type"] = () => new BadHttpRequestException("The body must be application/json.", StatusCodes.Status415UnsupportedMediaType),
        ["unexpected"] = () => new InvalidOperationException("connection string password=hunter2"),
    };

    private WebApplication app = null!;
    private HttpClient client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFluentCmsProblemDetails();

        app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey(SignedIn))
            {
                context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("test"));
            }

            await next();
        });
        app.UseExceptionHandler();
        app.MapGet("/throw/{name}", IResult (string name) => throw Throwers[name]());
        await app.StartAsync();

        client = app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }

    [Theory]
    [InlineData("denied", 401, "FCMS3102")]
    [InlineData("settings-denied", 401, "FCMS3102")]
    [InlineData("not-found", 404, "FCMS3104")]
    [InlineData("conflict", 412, "FCMS3105")]
    [InlineData("required", 422, "FCMS2101")]
    [InlineData("several", 422, "FCMS2100")]
    [InlineData("query", 400, "FCMS2202")]
    [InlineData("query-several", 400, "FCMS2100")]
    [InlineData("mixed", 422, "FCMS2100")]
    [InlineData("singleton", 409, "FCMS2401")]
    [InlineData("site-missing", 404, "FCMS2301")]
    [InlineData("settings-conflict", 412, "FCMS2808")]
    [InlineData("hook-plugin", 422, "ACME0001")]
    [InlineData("hook-engine", 409, "FCMS2405")]
    [InlineData("rich-text", 422, "FCMS2100")]
    [InlineData("bad-request", 400, "FCMS3101")]
    [InlineData("media-type", 415, "FCMS3107")]
    [InlineData("unexpected", 500, "FCMS3199")]
    public async Task AnExceptionBecomesItsStatusAndCode(string name, int status, string code)
    {
        var (response, problem) = await Get(name);

        ((int)response.StatusCode).ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("status").GetInt32().ShouldBe(status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("type").GetString().ShouldBe($"urn:fluentcms:error:{code}");
        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("denied", 403, "FCMS3103")]
    [InlineData("settings-denied", 403, "FCMS3103")]
    public async Task ASignedInPrincipalIsForbiddenNotUnauthorized(string name, int status, string code)
    {
        var (response, problem) = await Get(name, signedIn: true);

        ((int)response.StatusCode).ShouldBe(status);
        problem.GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task EveryProblemCarriesATraceId()
    {
        foreach (var name in Throwers.Keys)
        {
            var (_, problem) = await Get(name);

            problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace(name);
        }
    }

    [Fact]
    public async Task AValidationErrorListsEachPathAndCode()
    {
        var (_, problem) = await Get("several");

        var errors = problem.GetProperty("errors").EnumerateArray().Select(error => (
            error.GetProperty("path").GetString(),
            error.GetProperty("code").GetString(),
            error.GetProperty("message").GetString())).ToList();

        errors.ShouldBe(
        [
            ("title", "FCMS2101", "A value is required."),
            ("seo.metaTitle", "FCMS2103", "Must have at most 60 characters, has 80."),
            ("sections[2].heading", "FCMS2101", "A value is required."),
        ]);
        problem.GetProperty("detail").GetString()!.ShouldContain("3 errors");
    }

    [Fact]
    public async Task ASingleValidationErrorIsItsMessageAndStillAList()
    {
        var (_, problem) = await Get("required");

        problem.GetProperty("detail").GetString().ShouldBe("A value is required.");
        problem.GetProperty("errors").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task AVersionConflictIs412WithTheCurrentEntry()
    {
        var (response, problem) = await Get("conflict");

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var current = problem.GetProperty("current");
        current.GetProperty("title").GetString().ShouldBe("Stored");
        current.GetProperty("views").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task AVersionConflictOnAnEntryThePrincipalMayNotReadHasNoCurrent()
    {
        var (response, problem) = await Get("conflict-hidden");

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        problem.TryGetProperty("current", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("required")]
    [InlineData("not-found")]
    public async Task OnlyAValidationErrorHasErrorsAndOnlyAConflictHasCurrent(string name)
    {
        var (_, problem) = await Get(name);

        problem.TryGetProperty("current", out _).ShouldBeFalse();
        problem.TryGetProperty("errors", out _).ShouldBe(name == "required");
    }

    [Fact]
    public async Task AnUnexpectedErrorDoesNotLeakItsMessage()
    {
        var (_, problem) = await Get("unexpected");

        problem.GetProperty("detail").GetString()!.ShouldNotContain("hunter2");
    }

    [Fact]
    public async Task AClientThatAcceptsOnlyHtmlStillGetsProblemJson()
    {
        var (response, problem) = await Get("not-found", accept: "text/html");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("code").GetString().ShouldBe("FCMS3104");
    }

    private async Task<(HttpResponseMessage Response, JsonElement Problem)> Get(string name, bool signedIn = false, string? accept = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/throw/{name}");
        if (signedIn)
        {
            request.Headers.Add(SignedIn, "1");
        }

        if (accept is not null)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response, JsonDocument.Parse(body).RootElement.Clone());
    }
}
