namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Base interface for database manager markers used in database configuration.
/// Library markers identify which database configuration to use for all services within a class library.
/// This interface serves as a type constraint to ensure only valid marker interfaces can be used
/// with IDatabaseScopeMarker&lt;T&gt;.
/// </summary>
public interface IDatabaseScopeMarker
{
    // Empty interface - serves purely as a marker and type constraint
}
