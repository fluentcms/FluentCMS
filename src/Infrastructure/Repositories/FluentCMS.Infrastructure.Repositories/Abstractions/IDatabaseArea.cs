namespace FluentCMS.Infrastructure.Repositories.Abstractions;

// Marker interface to define database scope/context for repository configuration
// Implementations of this interface are used to group entities to specific databases

public interface IDatabaseArea
{
    // This is a marker interface - no methods required
    // Specific database scopes inherit from this interface to create type-safe database groupings
}
