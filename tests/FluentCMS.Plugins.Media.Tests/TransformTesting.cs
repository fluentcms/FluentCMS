using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Media;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Shouldly;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

internal static class Transforms
{
    public static readonly Rgba32 Red = new(255, 0, 0);
    public static readonly Rgba32 Green = new(0, 255, 0);
    public static readonly Rgba32 Blue = new(0, 0, 255);
    public static readonly Rgba32 Yellow = new(255, 255, 0);

    // A media host whose image processor is the default wrapped in a RecordingProcessor.
    public static MediaHost RecordingHost(Action<ProcessorCalls>? configure = null)
    {
        var media = new MediaHost();
        var calls = new ProcessorCalls();
        configure?.Invoke(calls);
        media.Host
            .WithServices(services => services.AddSingleton(calls))
            .Configure(cms => cms.Providers.Use<IImageProcessor, RecordingProcessor>());
        return media;
    }

    public static ProcessorCalls Calls(MediaHost media) => media.Services.GetRequiredService<ProcessorCalls>();

    // Four bands from left to right, a quarter of the width each: red, green, blue and yellow.
    public static byte[] Bands(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        Rgba32[] colours = [Red, Green, Blue, Yellow];
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = colours[x * 4 / width];
                }
            }
        });
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    public static async Task<MediaAsset> UploadAsync(HttpClient admin, byte[] bytes, string name = "photo.png")
    {
        var items = await UploadedAsync(admin, null, (name, bytes));
        return items[0].Deserialize<MediaAsset>(JsonSerializerOptions.Web)!;
    }

    public static async Task<string> UrlAsync(MediaHost media, MediaAsset asset, MediaImageOptions? options = null)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaService>().GetImageUrlAsync(asset, options, Token);
    }

    public static async Task<HttpResponseMessage> FetchAsync(HttpClient client, string url, string? accept = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        return await client.SendAsync(request, Token);
    }

    public static async Task<(string? Type, byte[] Bytes)> ImageAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);
        response.IsSuccessStatusCode.ShouldBeTrue(System.Text.Encoding.UTF8.GetString(bytes));
        return (response.Content.Headers.ContentType?.MediaType, bytes);
    }

    public static Image<Rgba32> Decode(byte[] bytes) => Image.Load<Rgba32>(bytes);

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
        return json["errors"]?.AsArray().FirstOrDefault()?["code"]?.GetValue<string>() ?? json["code"]?.GetValue<string>();
    }

    // The URL with one parameter of its query changed, keeping its signature.
    public static string WithParameter(string url, string name, string value)
    {
        var parts = url.Split('?', 2);
        var pairs = parts[1].Split('&').Select(pair => pair.Split('=', 2)).ToList();
        var index = pairs.FindIndex(pair => pair[0] == name);
        if (index < 0)
        {
            pairs.Insert(0, [name, value]);
        }
        else
        {
            pairs[index] = [name, value];
        }

        return $"{parts[0]}?{string.Join('&', pairs.Select(pair => $"{pair[0]}={pair[1]}"))}";
    }
}

internal sealed class ProcessorCalls
{
    private int _identified;
    private int _processed;

    public bool EncodesAvif { get; set; }

    public int Identified => Volatile.Read(ref _identified);

    public int Processed => Volatile.Read(ref _processed);

    public void Identify() => Interlocked.Increment(ref _identified);

    public void Process() => Interlocked.Increment(ref _processed);
}

// The default processor, counting its calls; with EncodesAvif, it also claims AVIF and writes four bytes for it.
internal sealed class RecordingProcessor(ProcessorCalls calls) : IImageProcessor
{
    private readonly ImageSharpImageProcessor _inner = new();

    public bool CanDecode(string mimeType) => _inner.CanDecode(mimeType);

    public bool CanEncode(string mimeType) => (calls.EncodesAvif && mimeType == "image/avif") || _inner.CanEncode(mimeType);

    public Task<ImageHeader?> IdentifyAsync(Stream image, CancellationToken cancellationToken = default)
    {
        calls.Identify();
        return _inner.IdentifyAsync(image, cancellationToken);
    }

    public async Task ProcessAsync(Stream image, ImageTransform transform, Stream output, CancellationToken cancellationToken = default)
    {
        calls.Process();
        if (transform.MimeType == "image/avif")
        {
            await output.WriteAsync("AVIF"u8.ToArray(), cancellationToken);
            return;
        }

        await _inner.ProcessAsync(image, transform, output, cancellationToken);
    }
}
