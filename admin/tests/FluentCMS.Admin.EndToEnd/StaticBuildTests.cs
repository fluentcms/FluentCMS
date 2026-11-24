using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.EndToEnd.MediaUi;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The prebuilt static build of admin spec 6.3 as an operator takes it from a release: the case publishes the admin, makes the
// zip with eng/admin-zip.cs as the Publish workflow does, unzips it, edits config.json and the <base href> of index.html, and
// serves it from another site and under a path with a file server of its own, the framework's static files, with what
// docs/admin-hosting.md asks of any host. The API host lists that origin and the client's redirect URIs (EndToEndHost, spec
// 6.4). The run on the image leaves the case out by its trait: it checks the zip, which the image does not hold.
[Trait("Image", "LeftOut")]
public sealed class StaticBuildTests(EndToEndHost host) : BrowserTest(host)
{
    private const string Icon = "_content/FluentCMS.Admin.Components/favicon.svg";

    // The publish of the app with no build, under the load of the other test apps of a full run.
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheUnzippedBuildSignsAnEditorInFromAnotherOriginReadsAListShowsAnImageAndSignsOut()
    {
        var browser = await OpenBrowserAsync();
        await using var site = await StaticSite.StartAsync(Host, await BuildAsync());
        var tab = await browser.NewTabAsync();
        var favicons = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) =>
        {
            if (new Uri(request.Url).AbsolutePath == "/favicon.ico")
            {
                favicons.Enqueue(request.Url);
            }
        };

        // The sign-in returns to the static build's own callback, which the configured client lists.
        var list = new Uri(Host.StaticAdmin, "content/article?pageSize=3").AbsoluteUri;
        await tab.Page.GotoAsync(list);
        var editor = await EditorAsync();
        await browser.SignInOnTheHostedPageAsync(tab, editor);
        HttpUtility.ParseQueryString(tab.AuthorizeRequests.First().Query)["redirect_uri"].ShouldBe(new Uri(Host.StaticAdmin, "authentication/login-callback").AbsoluteUri);

        var grid = tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = "Article", Exact = true });
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(3);
        tab.Page.Url.ShouldStartWith(list);

        // Without faviconUrl the page names the built-in icon, so the browser asks for no /favicon.ico.
        await Expect(tab.Page.Locator("head link[rel='icon']").First).ToHaveAttributeAsync("href", Icon);
        (await tab.Page.EvaluateAsync<int>("async icon => (await fetch(icon)).status", Icon)).ShouldBe(200);

        // A client route loaded anew is the fallback's index.html, and the session comes back through the token endpoint of
        // the API's origin.
        var restored = await tab.TokenRequestOfAsync(() => tab.Page.GotoAsync(new Uri(Host.StaticAdmin, "media").AbsoluteUri));
        restored.ShouldBeGranted("refresh_token");
        var main = tab.Page.Locator("main");
        await Expect(main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Page.Heading"), Exact = true })).ToBeVisibleAsync();

        // The thumbnail's signed URL is resolved against apiBaseUrl, which the page's img-src allows.
        await UploadAsync(main, PngFile($"{Mark}-photo.png"));
        await SearchAsync(main, Mark);
        await Expect(tab.Page.Locator(".media-tile-name")).ToHaveTextAsync([$"{Mark}-photo.png"]);
        var image = tab.Page.Locator(".media-tile img");
        await Expect(image).Not.ToHaveJSPropertyAsync("naturalWidth", 0);
        (await image.GetAttributeAsync("src")).ShouldStartWith(Host.Origin.AbsoluteUri);

        // The hosted logout page returns to the static build's base, which finds no session and signs in again.
        var authorizations = tab.AuthorizeRequests.Count;
        await tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = editor.Email }).ClickAsync();
        await tab.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Repository.AdminText("Shell.UserMenu.SignOut"), Exact = true }).ClickAsync();
        await tab.Page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/account/logout");
        await tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out", Exact = true }).ClickAsync();
        await Expect(tab.LoginField).ToBeVisibleAsync();
        tab.AuthorizeRequests.Count.ShouldBeGreaterThan(authorizations);
        HttpUtility.ParseQueryString(tab.AuthorizeRequests.Last().Query)["redirect_uri"].ShouldBe(new Uri(Host.StaticAdmin, "authentication/login-callback").AbsoluteUri);

        favicons.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The zip as a release attaches it, unzipped, with config.json pointing at the API host and the base at StaticPath.
    private async Task<DirectoryInfo> BuildAsync()
    {
        var work = Host.NewDirectory();
        var publish = Path.Combine(work.FullName, "publish");
        var app = Path.Combine(Repository.Root, "admin", "src", "FluentCMS.Admin.App", "FluentCMS.Admin.App.csproj");
        await DotnetAsync("Publishing the admin app", ["publish", app, "-c", Repository.Configuration, "--no-build", "-o", publish, "-p:WasmBuildNative=false", "-nologo", "-nodeReuse:false"]);
        await DotnetAsync("Making the zip", ["run", Path.Combine(Repository.Root, "eng", "admin-zip.cs"), "--", "pack", publish, "e2e", work.FullName]);

        var site = work.CreateSubdirectory("site");
        ZipFile.ExtractToDirectory(Path.Combine(work.FullName, "fluentcms-admin-e2e.zip"), site.FullName);

        var configurationFile = Path.Combine(site.FullName, "config.json");
        var configuration = JsonNode.Parse(await File.ReadAllTextAsync(configurationFile, Token))!.AsObject();
        configuration["apiBaseUrl"] = Host.Origin.GetLeftPart(UriPartial.Authority);
        configuration["authority"] = Host.Origin.AbsoluteUri;
        await File.WriteAllTextAsync(configurationFile, configuration.ToJsonString(), Token);

        var indexFile = Path.Combine(site.FullName, "index.html");
        var index = await File.ReadAllTextAsync(indexFile, Token);
        index.ShouldContain("<base href=\"/\" />");
        await File.WriteAllTextAsync(indexFile, index.Replace("<base href=\"/\" />", $"<base href=\"{EndToEndHost.StaticPath}\" />", StringComparison.Ordinal), Token);
        return site;
    }

    private static async Task DotnetAsync(string step, string[] arguments)
    {
        var (exitCode, _, tail) = await ChildProcess.RunAsync(
            "dotnet", arguments, Repository.Root, new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1" }, BuildTimeout, input: null, Token);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{step} exited with {exitCode}.{Environment.NewLine}{tail}");
        }
    }

    // The requirements of docs/admin-hosting.md: a path that is no file answers index.html, but under _framework/ and _content/;
    // index.html and config.json are never stored, the fingerprinted framework files are kept for good and the rest revalidates;
    // and every answer carries the page's headers, with the API's origin in img-src and connect-src.
    private sealed class StaticSite(WebApplication app, X509Certificate2 certificate) : IAsyncDisposable
    {
        public static async Task<StaticSite> StartAsync(EndToEndHost host, DirectoryInfo root)
        {
            var certificate = host.LoadServerCertificate();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(host.StaticOrigin.Port, listen => listen.UseHttps(certificate)));
            var app = builder.Build();

            var files = new PhysicalFileProvider(root.FullName);
            var api = host.Origin.GetLeftPart(UriPartial.Authority);
            var policy = "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; "
                + $"img-src 'self' data: blob: {api}; font-src 'self'; connect-src 'self' {api}; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            app.Map(EndToEndHost.StaticPath.TrimEnd('/'), site =>
            {
                site.Use((context, next) =>
                {
                    var headers = context.Response.Headers;
                    headers.ContentSecurityPolicy = policy;
                    headers.XContentTypeOptions = "nosniff";
                    headers["Referrer-Policy"] = "no-referrer";
                    headers["Cross-Origin-Opener-Policy"] = "same-origin";
                    return next(context);
                });
                site.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = files,
                    ServeUnknownFileTypes = true,
                    DefaultContentType = "application/octet-stream",
                    OnPrepareResponse = file => file.Context.Response.Headers.CacheControl = CacheControl(file.Context.Request.Path),
                });
                site.Run(async context =>
                {
                    var path = context.Request.Path;
                    if (path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                    {
                        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                        return;
                    }

                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-store";
                    await context.Response.SendFileAsync(files.GetFileInfo("index.html"), context.RequestAborted);
                });
            });

            await app.StartAsync(Token);
            return new StaticSite(app, certificate);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
            certificate.Dispose();
        }

        // Every name under _framework/ carries a fingerprint but its two entry scripts'.
        private static string CacheControl(PathString path) =>
            path.Value is "/index.html" or "/config.json" ? "no-store"
            : path.StartsWithSegments("/_framework", StringComparison.Ordinal) && path.Value is not ("/_framework/blazor.webassembly.js" or "/_framework/dotnet.js") ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
}
