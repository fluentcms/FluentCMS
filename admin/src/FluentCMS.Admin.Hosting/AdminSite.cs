using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Hosting;

// What MapFluentCmsAdmin answers besides the app's files: index.html with its base set, config.json from FluentCms:Admin,
// and the page's Content-Security-Policy. The addresses come from FluentCms:PublicOrigin, or from the request in
// Development only, so that a forged Host never reaches a document; without either the host does not start, refused with
// FCMS1105, or by AdminClientRegistration with FCMS1101 once AllowRequestOrigin lifts that one. The client options are read at the first config.json and not when the admin is mapped, so that a host
// building for a command, such as an init container's migrate, needs no public origin.
internal sealed partial class AdminSite(AdminOptions options, PublicOrigin origin, ReverseProxyOptions proxy, IOptions<ClientOptions> clients, IWebHostEnvironment environment)
{
    public const string Path = "/admin";
    public const string CallbackPath = "/authentication/login-callback";
    public const string IndexFile = "index.html";
    public const string ConfigurationFile = "config.json";

    // Admin spec 2.4.
    public static readonly string[] Scopes = ["openid", "profile", "email", "offline_access", "management"];

    private Page? _index;

    // FluentCms:ReverseProxy:PathBase as configured, empty when none.
    public string PathBase { get; } = proxy.PathBase ?? "";

    // The path base and /admin/, which the page's <base href> and so every address of the app resolve under.
    public string BaseHref { get; } = new PathString(proxy.PathBase).Add(Path).ToUriComponent() + "/";

    public byte[] Configuration(HttpRequest request)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("apiBaseUrl", ApiBaseUrl(request));
            json.WriteString("authority", Authority(request));
            json.WriteString("clientId", clients.Value.AdminClientId);
            json.WriteStartArray("scopes");
            foreach (var scope in Scopes)
            {
                json.WriteStringValue(scope);
            }

            json.WriteEndArray();

            var branding = options.Branding;
            json.WriteStartObject("branding");
            WriteIfSet(json, "name", branding.Name);
            WriteIfSet(json, "logoUrl", branding.LogoUrl);
            WriteIfSet(json, "faviconUrl", branding.FaviconUrl);
            WriteIfSet(json, "accentColor", branding.AccentColor);
            json.WriteEndObject();

            var cultures = options.Cultures;
            json.WriteStartObject("cultures");
            if (cultures.Offered.Count > 0)
            {
                json.WriteStartArray("offered");
                foreach (var culture in cultures.Offered)
                {
                    json.WriteStringValue(culture);
                }

                json.WriteEndArray();
            }

            WriteIfSet(json, "default", cultures.Default);
            json.WriteEndObject();

            json.WriteStartArray("previewOrigins");
            foreach (var preview in PreviewOrigins())
            {
                json.WriteStringValue(preview);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    // As narrow as the app allows (measured in #3148): the framework runs WebAssembly, which needs 'wasm-unsafe-eval' and
    // no other eval; the component library writes a <style> element for the drawer and style attributes on the cells of
    // the data grid, which need 'unsafe-inline' in style-src. The API and the authority are the origins the app calls,
    // config.json is its own, and images come from the API (signed asset URLs) and from a logo or favicon on another origin.
    public string ContentSecurityPolicy(HttpContext context)
    {
        var request = context.Request;
        var api = OriginOf(ApiBaseUrl(request));
        var authority = OriginOf(Authority(request));
        var images = options.Branding.AbsoluteUrls().Select(url => url.GetLeftPart(UriPartial.Authority)).Prepend(api);
        var frames = PreviewOrigins().ToList();

        var policy = new StringBuilder("default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; ")
            .Append("img-src 'self' data: blob:").Append(Sources(images)).Append("; ")
            .Append("font-src 'self'; ")
            .Append("connect-src 'self'").Append(Sources([api, authority]));

        // dotnet watch adds a script to the page that reloads it and applies hot reload over a WebSocket of its own.
        if (environment.IsDevelopment())
        {
            policy.Append(" ws://localhost:* wss://localhost:*");
        }

        policy.Append("; ");
        if (frames.Count > 0)
        {
            policy.Append("frame-src").Append(Sources(frames)).Append("; ");
        }

        return policy.Append("base-uri 'self'; form-action 'self'; frame-ancestors 'none'").ToString();
    }

    // Reads index.html when the admin is mapped, so that an app without the file or without a single <base href> stops the
    // host at start rather than on the first request.
    public void LoadIndex()
    {
        var file = IndexFileInfo();
        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        _index = new Page(file.LastModified, file.Length, Rewrite(reader.ReadToEnd()));
    }

    // index.html as the app was built, with only the href of its <base> changed. It is read again when the file changes, as
    // under dotnet watch.
    public async Task<string> IndexAsync(CancellationToken cancellationToken)
    {
        var file = IndexFileInfo();
        if (_index is { } cached && cached.LastModified == file.LastModified && cached.Length == file.Length)
        {
            return cached.Content;
        }

        string html;
        await using (var stream = file.CreateReadStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            html = await reader.ReadToEndAsync(cancellationToken);
        }

        var content = Rewrite(html);
        _index = new Page(file.LastModified, file.Length, content);
        return content;
    }

    private IFileInfo IndexFileInfo()
    {
        var file = environment.WebRootFileProvider.GetFileInfo(IndexFile);
        return file.Exists
            ? file
            : throw FluentCmsConfigurationException.NotRegistered(
                $"MapFluentCmsAdmin found no {IndexFile} among the host's static web assets. A host that references FluentCMS.Admin.Hosting as a package gets the first-party build with it; one that references an admin app's project, such as FluentCMS.Admin.App, gets that app's files.");
    }

    private string Rewrite(string html)
    {
        var bases = BaseElement().Matches(html);
        if (bases.Count != 1)
        {
            throw FluentCmsConfigurationException.MalformedFile(
                $"The admin's {IndexFile} has {bases.Count} <base href> elements; MapFluentCmsAdmin needs exactly one, whose href it sets to {BaseHref}.");
        }

        var href = bases[0].Groups["href"];
        return string.Concat(html.AsSpan(0, href.Index), HtmlEncoder.Default.Encode(BaseHref), html.AsSpan(href.Index + href.Length));
    }

    private string HostBase(HttpRequest request) =>
        origin.Base
        ?? (environment.IsDevelopment()
            ? $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}"
            : throw new InvalidOperationException($"{AdminOptions.Root}:{PublicOrigin.Key} is not set, and outside Development the admin is not built from a request's host."));

    private string ApiBaseUrl(HttpRequest request) =>
        string.IsNullOrEmpty(options.ApiBaseUrl) ? HostBase(request) : options.ApiBaseUrl;

    // The issuer as the discovery document writes it, which the admin compares as a string: the public origin's, or the
    // request's base in Development, written as a URI, as OpenIddict writes the issuer it builds from either.
    private string Authority(HttpRequest request) => (origin.Issuer ?? new Uri(HostBase(request), UriKind.Absolute)).AbsoluteUri;

    private IEnumerable<string> PreviewOrigins() =>
        options.PreviewOrigins.Select(AdminOptions.Origin).OfType<string>().Distinct(StringComparer.Ordinal);

    private static string OriginOf(string url) => new Uri(url, UriKind.Absolute).GetLeftPart(UriPartial.Authority);

    private static string Sources(IEnumerable<string> origins) =>
        string.Concat(origins.Distinct(StringComparer.OrdinalIgnoreCase).Select(source => " " + source));

    private static void WriteIfSet(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            json.WriteString(name, value);
        }
    }

    [GeneratedRegex("""<base\s+href\s*=\s*"(?<href>[^"]*)"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex BaseElement();

    private sealed record Page(DateTimeOffset LastModified, long Length, string Content);
}
