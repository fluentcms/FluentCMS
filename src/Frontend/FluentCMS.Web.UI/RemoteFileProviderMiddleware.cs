using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.UI;

public class RemoteFileProviderMiddleware
{
    private const string _tempFolder = "temp";

    public RemoteFileProviderMiddleware(RequestDelegate next)
    {
        if (!Directory.Exists(_tempFolder))
            Directory.CreateDirectory(_tempFolder);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var url = $"{request.Scheme}://{request.Host}{request.Path.Value}";

        // hash the url to get a unique key
        var key = url.GetHashCode().ToString();

        // Check if the file is already in the temp folder
        var tempFile = Path.Combine(_tempFolder, key);
        if (!File.Exists(tempFile))
        {
            // Retrieve the IFileClient service from HttpContext.RequestServices
            var fileClient = context.RequestServices.GetRequiredService<IFileClient>();

            // Use the file client to download the file response
            var response = await fileClient.DownloadGetResponseMessageAsync(url, context.RequestAborted);

            if (response.IsSuccessStatusCode)
            {
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

                await using var responseStream = await response.Content.ReadAsStreamAsync(context.RequestAborted);

                // save the file to the temp folder
                await using var tempFileStream = File.Create(tempFile);
                await responseStream.CopyToAsync(tempFileStream, context.RequestAborted);
            }
            else
            {
                context.Response.StatusCode = (int)response.StatusCode;
                return;
            }
        }

        // extract file name from url
        var fileName = url.Split('/').Last();
        context.Response.ContentType = GetContentType(fileName);
        await using var fileStream = File.OpenRead(tempFile);
        await fileStream.CopyToAsync(context.Response.Body, context.RequestAborted);
        return;        
    }

    private static string GetContentType(string fileName)
    {
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fileName, out var contentType))
        {
            // If the file extension is not recognized, default to a generic content type
            contentType = "application/octet-stream";
        }
        return contentType;
    }

}
