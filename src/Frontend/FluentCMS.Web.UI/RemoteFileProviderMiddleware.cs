using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.UI;

public class RemoteFileProviderMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var url = $"{request.Scheme}://{request.Host}{request.Path.Value}";

        // Retrieve the IFileClient service from HttpContext.RequestServices
        var fileClient = context.RequestServices.GetRequiredService<IFileClient>();

        // Use the file client to download the file response
        var response = await fileClient.DownloadGetResponseMessageAsync(url, context.RequestAborted);

        if (response.IsSuccessStatusCode)
        {
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

            // TODO: think of a way for caching to be configurable
            // Set Cache-Control header to cache file indefinitely, assuming content will not change
            //context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";

            await using var responseStream = await response.Content.ReadAsStreamAsync(context.RequestAborted);

            await responseStream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        else
        {
            context.Response.StatusCode = (int)response.StatusCode;
            return;
        }
    }
}
