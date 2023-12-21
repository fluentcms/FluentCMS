using FluentCMS.Services;

namespace FluentCMS;

public class SetupMiddleware(RequestDelegate next)
{
    private static bool? _isInitialized;
    private static readonly SemaphoreSlim _initializationSemaphore = new(1, 1);

    public async Task InvokeAsync(HttpContext context, ISystemSettingsService systemSettingsService)
    {
        //// Check if the initialization status is already determined
        //if (!_isInitialized.HasValue)
        //{
        //    // Wait to enter the semaphore (if another request is already performing initialization, it will block here)
        //    await _initializationSemaphore.WaitAsync();

        //    try
        //    {
        //        // Double-check the flag to see if initialization was completed while this request was waiting.
        //        if (!_isInitialized.HasValue)
        //        {
        //            _isInitialized = await systemSetupService.IsInitialized();

        //            // Perform the initialization if needed
        //            if (!_isInitialized.Value)
        //            {
        //                // Mark as initialized
        //                _isInitialized = true;
        //            }
        //        }
        //    }
        //    finally
        //    {
        //        // Release the semaphore so other requests can proceed
        //        _initializationSemaphore.Release();
        //    }
        //}

        //if (!_isInitialized.Value)
        //{
        //    // Return a message or perform an action indicating the app is not initialized
        //    context.Response.ContentType = "text/plain";
        //    await context.Response.WriteAsync("App is not initialized. Please set up.");
        //    return;
        //}

        await next(context);
    }
}
