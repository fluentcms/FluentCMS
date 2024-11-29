using FluentCMS.Entities.Logging;
using FluentCMS.Repositories.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FluentCMS.Web.Api.Middleware;

internal sealed class HttpLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public HttpLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, IHttpLogRepository repository, IApiExecutionContext apiContext)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            var requestBody = await ReadRequestBody(context.Request);

            var originalResponseStream = context.Response.Body;
            await using var responseMemoryStream = new MemoryStream();
            context.Response.Body = responseMemoryStream;

            Exception exception = null;

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                var responseBody = await ReadResponseBody(context, originalResponseStream, responseMemoryStream);

                stopwatch.Stop();

                var logModel = CreateLogModel(context, apiContext, requestBody, responseBody, stopwatch.ElapsedMilliseconds, exception != null ? 500 : null, exception);
                await Log(logModel, repository);
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    private async Task<string> ReadRequestBody(HttpRequest request)
    {
        request.EnableBuffering();

        using var reader = new StreamReader(request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        return requestBody;
    }

    private async Task<string> ReadResponseBody(HttpContext context, Stream originalResponseStream, Stream memoryStream)
    {
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream, encoding: Encoding.UTF8);
        var responseBody = await reader.ReadToEndAsync();
        memoryStream.Position = 0;
        await memoryStream.CopyToAsync(originalResponseStream);
        context.Response.Body = originalResponseStream;

        return responseBody;
    }

    private static HttpLog CreateLogModel(HttpContext context, IApiExecutionContext apiContext, string requestBody, string responseBody, long duration, int? statusCode = null, Exception exception = null)
    {
        exception ??= context.Features.Get<IExceptionHandlerFeature>()?.Error;

        return new HttpLog
        {
            Request = new HttpRequestLog(context.Request, requestBody),
            Response = new HttpResponseLog(context.Response, responseBody),
            StatusCode = statusCode ?? context.Response.StatusCode,
            Duration = duration,
            Context = apiContext,
            Exception = exception == null ? null : new ExceptionModel(exception)
        };
    }

    private static async Task Log(HttpLog log, IHttpLogRepository repository)
    {
        await repository.Create(log);
    }
}
