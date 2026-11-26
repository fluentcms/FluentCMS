namespace FluentCMS.Security;

/// <summary>
/// One value a plugin stores protected with Data Protection, named by where it is stored. The command reports a value by its
/// table, id and column alone, never by what is stored.
/// </summary>
public sealed class ProtectedValue
{
    /// <summary>Creates the description of a stored value.</summary>
    /// <param name="table">The table that holds it, such as <c>cms_webhooks_subscription</c>.</param>
    /// <param name="id">The id of its row, as text.</param>
    /// <param name="column">The column that holds it.</param>
    /// <param name="purpose">The purpose it was protected under, given to <c>IDataProtectionProvider.CreateProtector</c>.</param>
    /// <param name="stored">What is stored: the text <c>IDataProtector.Protect(string)</c> returned.</param>
    /// <exception cref="ArgumentException">The table, id, column or purpose is empty, or what is stored is.</exception>
    public ProtectedValue(string table, string id, string column, string purpose, string stored)
    {
        ArgumentException.ThrowIfNullOrEmpty(table);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(column);
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentException.ThrowIfNullOrEmpty(stored);

        Table = table;
        Id = id;
        Column = column;
        Purpose = purpose;
        Stored = stored;
    }

    /// <summary>The table that holds the value.</summary>
    public string Table { get; }

    /// <summary>The id of its row, as text.</summary>
    public string Id { get; }

    /// <summary>The column that holds the value.</summary>
    public string Column { get; }

    /// <summary>The purpose the value was protected under.</summary>
    public string Purpose { get; }

    /// <summary>What is stored: the text <c>IDataProtector.Protect(string)</c> returned.</summary>
    public string Stored { get; }
}
