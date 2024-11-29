using FluentCMS.Entities.Logging;
using FluentCMS.Repositories.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace FluentCMS.Web.Api.Middleware;

internal sealed class HttpLoggingMiddleware
{
    private readonly RequestDelegate _next;

#pragma warning disable IDE0290 // Use primary constructor
    public HttpLoggingMiddleware(RequestDelegate next)
#pragma warning restore IDE0290 // Use primary constructor
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, IHttpLogRepository repository, IApiExecutionContext apiContext)
    {
        var stopwatch = Stopwatch.StartNew();

        var requestBody = await ReadRequestBody(context.Request);

        var originalResponseStream = context.Response.Body;
        await using var responseMemoryStream = new MemoryStream();
        context.Response.Body = responseMemoryStream;

        Exception? exception = null;

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
            stopwatch.Stop();

            var responseBody = await ReadResponseBody(context, originalResponseStream, responseMemoryStream);

            exception ??= context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var assembly = Assembly.GetEntryAssembly();
            var assemblyName = assembly?.GetName();
            var process = Process.GetCurrentProcess();
            var thread = Thread.CurrentThread;

            var httpLog = new HttpLog
            {
                Request = GetHttpRequestLog(context.Request, requestBody),
                Response = GetHttpResponseLog(context.Response, responseBody),
                StatusCode = context.Response.StatusCode,
                Duration = stopwatch.ElapsedMilliseconds,
                Exception = GetHttpException(exception),
                AssemblyName = assemblyName?.Name ?? string.Empty,
                AssemblyVersion = assemblyName?.Version?.ToString() ?? string.Empty,
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                ThreadId = thread.ManagedThreadId,
                MemoryUsage = process.PrivateMemorySize64,
                MachineName = Environment.MachineName,
                EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? string.Empty,
                EnvironmentUserName = Environment.UserName,
                ApiTokenKey = apiContext.ApiTokenKey,
                IsAuthenticated = apiContext.IsAuthenticated,
                Language = apiContext.Language,
                SessionId = apiContext.SessionId,
                StartDate = apiContext.StartDate,
                TraceId = apiContext.TraceId,
                UniqueId = apiContext.UniqueId,
                UserId = apiContext.UserId,
                UserIp = apiContext.UserIp,
                Username = apiContext.Username
            };

            await repository.Create(httpLog);

        }
    }

    private static async Task<string> ReadRequestBody(HttpRequest request)
    {
        request.EnableBuffering();

        using var reader = new StreamReader(request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        return requestBody;
    }

    private static async Task<string> ReadResponseBody(HttpContext context, Stream originalResponseStream, Stream memoryStream)
    {
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream, encoding: Encoding.UTF8);
        var responseBody = await reader.ReadToEndAsync();
        memoryStream.Position = 0;
        await memoryStream.CopyToAsync(originalResponseStream);
        context.Response.Body = originalResponseStream;

        return responseBody;
    }

    private static HttpRequestLog GetHttpRequestLog(HttpRequest request, string requestBody)
    {
        return new HttpRequestLog
        {
            DisplayUrl = request.GetDisplayUrl(),
            Protocol = request.Protocol,
            Method = request.Method,
            Scheme = request.Scheme,
            PathBase = request.PathBase,
            Path = request.Path,
            QueryString = request.QueryString.Value ?? string.Empty,
            ContentType = request.ContentType ?? string.Empty,
            ContentLength = request.ContentLength,
            Headers = request.Headers?.ToDictionary(x => x.Key, x => x.Value.ToString()) ?? [],
            Body = requestBody
        };
    }

    private static HttpResponseLog GetHttpResponseLog(HttpResponse response, string responseBody)
    {
        return new HttpResponseLog
        {
            ContentType = response.ContentType ?? string.Empty,
            ContentLength = response.ContentLength,
            Body = responseBody,
            Headers = response.Headers?.ToDictionary(x => x.Key, x => x.Value.ToString()) ?? []
        };
    }

    private static HttpException? GetHttpException(Exception? exception)
    {
        if (exception == null)
            return null;

        return new HttpException
        {
            Data = exception.Data,
            HelpLink = exception.HelpLink ?? string.Empty,
            HResult = exception.HResult,
            Message = exception.Message ?? string.Empty,
            Source = exception.Source ?? string.Empty,
            StackTrace = exception.StackTrace ?? string.Empty,
        };
    }
}
