namespace FluentCMS.Api.Middlewares;

public class JwtAuthorizationMiddleware
{
    // The next middleware in the request pipeline
    private readonly RequestDelegate _next;

    /// <summary>
    /// Constructor to initialize the middleware with the next delegate in the pipeline.
    /// </summary>
    /// <param name="next">The next delegate/middleware in the request pipeline.</param>
#pragma warning disable IDE0290 // Use primary constructor
    public JwtAuthorizationMiddleware(RequestDelegate next)
#pragma warning restore IDE0290 // Use primary constructor
    {
        _next = next; // Store the next middleware delegate
    }

    /// <summary>
    /// The method that processes each HTTP request.
    /// It checks for a JWT token in the Authorization header, authenticates it,
    /// and sets the HttpContext.User if the token is valid.
    /// </summary>
    /// <param name="context">The HttpContext for the current request.</param>
    /// <param name="authenticationService">The authentication service used to authenticate the JWT token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Invoke(HttpContext context, IAuthenticationService authenticationService)
    {
        // Check if the Authorization header is present in the request
        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            // Attempt to authenticate the JWT token using the JwtBearer scheme
            var result = await authenticationService.AuthenticateAsync(context, JwtBearerDefaults.AuthenticationScheme);

            // If authentication is successful, set the authenticated user's identity to the HttpContext
            if (result.Succeeded)
            {
                context.User = result.Principal;
            }
        }

        // Call the next middleware in the pipeline
        await _next(context);
    }
}