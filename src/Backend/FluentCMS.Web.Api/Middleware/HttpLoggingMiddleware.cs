using FluentCMS.Entities.Logging;
using FluentCMS.Repositories.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace FluentCMS.Web.Api.Middleware;

internal sealed class HttpLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpLogConfig _httpLogConfig;
    private readonly Assembly? _assembly;
    private readonly Process _process;
    private readonly AssemblyName? _assemblyName;

#pragma warning disable IDE0290 // Use primary constructor
    public HttpLoggingMiddleware(RequestDelegate next, IOptions<HttpLogConfig> options)
#pragma warning restore IDE0290 // Use primary constructor
    {
        _next = next;
        _httpLogConfig = options.Value ?? new HttpLogConfig();
        _assembly = Assembly.GetEntryAssembly();
        _process = Process.GetCurrentProcess();
        _assemblyName = _assembly?.GetName();
    }

    public async Task Invoke(HttpContext context, IHttpLogRepository repository)
    {
        if (!_httpLogConfig.Enable)
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        if (!_httpLogConfig.EnableResponseBody)
        {
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

                var httpLog = new HttpLog();

                // Create a list of tasks to run simultaneously
                var tasks = new[]
                {
                    FillHttpLog(httpLog, context, stopwatch),
                    FillRequest(httpLog, context.Request),
                    FillResponse(httpLog, context.Response, null),
                    FillException(httpLog, context, exception)
                };

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);
                await repository.Create(httpLog);
            }
        }

        if (_httpLogConfig.EnableResponseBody)
        {

            var originalResponseStream = context.Response.Body;

            await using var responseMemoryStream = new MemoryStream();
            context.Response.Body = responseMemoryStream;

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

                var httpLog = new HttpLog();

                // Create a list of tasks to run simultaneously
                var tasks = new[]
                {
                    FillHttpLog(httpLog, context, stopwatch),
                    FillRequest(httpLog, context.Request),
                    FillResponse(httpLog, context.Response, responseBody),
                    FillException(httpLog, context, exception)
                };

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);
                await repository.Create(httpLog);
            }
        }
    }

    private async Task<string> ReadRequestBody(HttpRequest request)
    {
        if (!_httpLogConfig.EnableRequestBody)
            return string.Empty;

        request.EnableBuffering();

        using var reader = new StreamReader(request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        return requestBody;
    }

    private static async Task<string> ReadResponseBody(HttpContext context, Stream originalResponseStream, Stream? memoryStream)
    {
        if (memoryStream == null)
            return string.Empty;

        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream, encoding: Encoding.UTF8);
        var responseBody = await reader.ReadToEndAsync();
        memoryStream.Position = 0;
        await memoryStream.CopyToAsync(originalResponseStream);
        context.Response.Body = originalResponseStream;

        return responseBody;
    }

    private async Task FillHttpLog(HttpLog httpLog, HttpContext context, Stopwatch stopwatch)
    {
        var thread = Thread.CurrentThread;
        var apiContext = context.RequestServices.GetRequiredService<IApiExecutionContext>();

        httpLog.StatusCode = context.Response.StatusCode;
        httpLog.Duration = stopwatch.ElapsedMilliseconds;
        httpLog.AssemblyName = _assemblyName?.Name ?? string.Empty;
        httpLog.AssemblyVersion = _assemblyName?.Version?.ToString() ?? string.Empty;
        httpLog.ProcessId = _process.Id;
        httpLog.ProcessName = _process.ProcessName;
        httpLog.ThreadId = thread.ManagedThreadId;
        httpLog.MemoryUsage = _process.PrivateMemorySize64;
        httpLog.MachineName = Environment.MachineName;
        httpLog.EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? string.Empty;
        httpLog.EnvironmentUserName = Environment.UserName;
        httpLog.ApiTokenKey = apiContext.ApiTokenKey;
        httpLog.IsAuthenticated = apiContext.IsAuthenticated;
        httpLog.Language = apiContext.Language;
        httpLog.SessionId = apiContext.SessionId;
        httpLog.StartDate = apiContext.StartDate;
        httpLog.TraceId = apiContext.TraceId;
        httpLog.UniqueId = apiContext.UniqueId;
        httpLog.UserId = apiContext.UserId;
        httpLog.UserIp = apiContext.UserIp;
        httpLog.Username = apiContext.Username;

        await Task.CompletedTask;
    }

    private async Task FillRequest(HttpLog httpLog, HttpRequest request)
    {
        var requestBody = _httpLogConfig.EnableRequestBody ? await ReadRequestBody(request) : null;

        httpLog.ReqUrl = request.GetDisplayUrl();
        httpLog.ReqProtocol = request.Protocol;
        httpLog.ReqMethod = request.Method;
        httpLog.ReqScheme = request.Scheme;
        httpLog.ReqPathBase = request.PathBase;
        httpLog.ReqPath = request.Path;
        httpLog.QueryString = request.QueryString.Value ?? string.Empty;
        httpLog.ReqContentType = request.ContentType ?? string.Empty;
        httpLog.ReqContentLength = request.ContentLength;
        httpLog.ReqBody = requestBody;
        httpLog.ReqHeaders = request.Headers?.ToDictionary(x => x.Key, x => x.Value.ToString()) ?? [];
    }

    private static async Task FillResponse(HttpLog httpLog, HttpResponse response, string? responseBody)
    {
        httpLog.ResContentType = response.ContentType ?? string.Empty;
        httpLog.ResContentLength = response.ContentLength;
        httpLog.ResBody = responseBody;
        httpLog.ResHeaders = response.Headers?.ToDictionary(x => x.Key, x => x.Value.ToString()) ?? [];
        await Task.CompletedTask;
    }
    private static async Task FillException(HttpLog httpLog, HttpContext context, Exception? exception)
    {
        exception ??= context.Features.Get<IExceptionHandlerFeature>()?.Error;

        if (exception == null)
            return;

        httpLog.ExData = exception.Data;
        httpLog.ExHelpLink = exception.HelpLink;
        httpLog.ExHResult = exception.HResult;
        httpLog.ExMessage = exception.Message;
        httpLog.ExSource = exception.Source;
        httpLog.ExStackTrace = exception.StackTrace;

        await Task.CompletedTask;
    }
}
