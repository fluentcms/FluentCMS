using System.Net;
using System.Text.Json;

namespace IdentityExample.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = new ApiResponse
        {
            Success = false,
            Timestamp = DateTime.UtcNow,
            TraceId = context.TraceIdentifier
        };

        switch (exception)
        {
            case ArgumentException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Message = "Invalid argument provided";
                break;
            
            case UnauthorizedAccessException:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                response.Message = "Unauthorized access";
                break;
            
            case NotImplementedException:
                response.StatusCode = (int)HttpStatusCode.NotImplemented;
                response.Message = "Feature not implemented";
                break;
            
            default:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Message = "An internal server error occurred";
                break;
        }

        // Add error details in development environment
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
        {
            response.Errors.Add(new ApiError("Exception", exception.Message));
            if (exception.InnerException != null)
            {
                response.Errors.Add(new ApiError("InnerException", exception.InnerException.Message));
            }
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = response.StatusCode;

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}
