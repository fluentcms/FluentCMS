using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.Api.Filters;

public class ApiAuthFilter : IAsyncAuthorizationFilter
{
    const string _header = "X-API-Key-Header";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var endpoints = context.ActionDescriptor.EndpointMetadata.OfType<ApiAuthAttribute>();

        if (!endpoints.Any())
            return;

        if (endpoints.Count() > 1)
        {
            var result = new
            {
                Status = "Error",
                Message = "Expected one attribute only"
            };

            context.Result = new ObjectResult(result)
            {
                StatusCode = StatusCodes.Status400BadRequest
            };

            return;
        }

        var endpoint = endpoints.First();

        var apiTokenService = context.HttpContext.RequestServices.GetRequiredService<IApiTokenService>();

        // Check if the required header is present
        if (!context.HttpContext.Request.Headers.ContainsKey(_header))
        {
            context.Result = new ForbidResult(); // Reject the request
            return;
        }

        var headerValue = context.HttpContext.Request.Headers[_header].ToString();

        // Check if the value exists in the database
        var isValid = await apiTokenService.IsApiKeyValidAsync(headerValue);

        if (!isValid)
        {
            context.Result = new ForbidResult(); // Reject the request
        }
    }
}

