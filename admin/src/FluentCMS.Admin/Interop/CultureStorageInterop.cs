using System.Globalization;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The copy of the user's interface culture that the next start reads before the host runs (spec 4.17); the saved preference
// stays the source. Storage the browser refuses reads as no copy and keeps nothing.
internal interface ICultureStorage
{
    ValueTask<string?> ReadAsync();

    // False when the browser did not keep it.
    ValueTask<bool> WriteAsync(string? culture);
}

// The allow-listed user of IJSRuntime for the copy (spec 3.1), through the browser's own localStorage functions. It writes one
// key and a culture name only, so that nothing else of the admin can reach localStorage through it; none is the key's absence.
internal sealed class CultureStorageInterop(IJSRuntime js, ILogger<CultureStorageInterop> logger) : ICultureStorage
{
    public const string Key = "fluentcms-admin/culture";

    // Before the host is built, through the runtime the builder registered already.
    public static ValueTask<string?> ReadAtStartAsync(IServiceCollection services) =>
        services.FirstOrDefault(service => service.ServiceType == typeof(IJSRuntime))?.ImplementationInstance is IJSRuntime runtime
            ? new CultureStorageInterop(runtime, NullLogger<CultureStorageInterop>.Instance).ReadAsync()
            : ValueTask.FromResult<string?>(null);

    public async ValueTask<string?> ReadAsync()
    {
        try
        {
            return Name(await js.InvokeAsync<string?>("localStorage.getItem", Key));
        }
        catch (JSException exception)
        {
            ShellLog.CultureNotKept(logger, exception);
            return null;
        }
    }

    public async ValueTask<bool> WriteAsync(string? culture)
    {
        try
        {
            if (Name(culture) is { } name)
            {
                await js.InvokeVoidAsync("localStorage.setItem", Key, name);
            }
            else
            {
                await js.InvokeVoidAsync("localStorage.removeItem", Key);
            }

            return true;
        }
        catch (JSException exception)
        {
            ShellLog.CultureNotKept(logger, exception);
            return false;
        }
    }

    private static string? Name(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(value).Name is { Length: > 0 } name ? name : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
