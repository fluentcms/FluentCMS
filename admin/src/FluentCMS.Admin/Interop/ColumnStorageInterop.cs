using System.Text.RegularExpressions;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The columns a user chose for the list of a type in a site, kept in the browser for every tab and the next visit (spec 4.4).
// Storage the browser refuses, as in some private windows, reads as no choice and keeps nothing.
internal interface IColumnStorage
{
    ValueTask<IReadOnlyList<string>?> ReadAsync(Guid user, Guid site, string type);

    ValueTask WriteAsync(Guid user, Guid site, string type, IReadOnlyList<string> columns);

    ValueTask RemoveAsync(Guid user, Guid site, string type);
}

// An allow-listed user of IJSRuntime for localStorage (spec 3.1), through the browser's own functions. Its keys hold a user,
// a site and a type name, and its values only column names, which are the names of fields and system members, so nothing
// else of the admin, and no token (AQ3), can reach localStorage through it.
internal sealed partial class ColumnStorageInterop(IJSRuntime js, ILogger<ColumnStorageInterop> logger) : IColumnStorage
{
    public const string Prefix = "fluentcms-admin/columns/";

    private const int MaxColumns = 64;

    public async ValueTask<IReadOnlyList<string>?> ReadAsync(Guid user, Guid site, string type)
    {
        if (Key(user, site, type) is not { } key)
        {
            return null;
        }

        try
        {
            var value = await js.InvokeAsync<string?>("localStorage.getItem", key);
            return value is null ? null : [.. value.Split(',').Where(IsName).Take(MaxColumns)];
        }
        catch (JSException exception)
        {
            ShellLog.ColumnsNotKept(logger, exception);
            return null;
        }
    }

    public async ValueTask WriteAsync(Guid user, Guid site, string type, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (Key(user, site, type) is not { } key || columns.Count > MaxColumns || !columns.All(IsName))
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, string.Join(',', columns));
        }
        catch (JSException exception)
        {
            ShellLog.ColumnsNotKept(logger, exception);
        }
    }

    public async ValueTask RemoveAsync(Guid user, Guid site, string type)
    {
        if (Key(user, site, type) is not { } key)
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch (JSException exception)
        {
            ShellLog.ColumnsNotKept(logger, exception);
        }
    }

    private static string? Key(Guid user, Guid site, string type) =>
        IsName(type) ? $"{Prefix}{user:N}/{site:N}/{type}" : null;

    // A field name is camelCase and a type name kebab-case (backend 4.2).
    private static bool IsName(string? name) => name is { Length: > 0 and <= 64 } && NamePattern().IsMatch(name);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
