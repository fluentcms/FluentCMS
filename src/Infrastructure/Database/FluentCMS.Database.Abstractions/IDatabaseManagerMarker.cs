namespace FluentCMS.Database.Abstractions;

/// <summary>
/// Base interface for database manager markers used in library-based database configuration.
/// 
/// Library markers identify which database configuration to use for all services within a class library.
/// This interface serves as a type constraint to ensure only valid marker interfaces can be used
/// with IDatabaseManager&lt;T&gt;.
/// 
/// To create a library marker:
/// 1. Create an interface that inherits from this interface
/// 2. Use that marker interface as the type parameter for IDatabaseManager&lt;T&gt; in your services
/// 3. Register the marker in your database configuration
/// 
/// Example:
/// <code>
/// // Define library marker
/// public interface IContentLibraryMarker : IDatabaseManagerMarker { }
/// 
/// // Use in services
/// public class ContentService
/// {
///     public ContentService(IDatabaseManager&lt;IContentLibraryMarker&gt; databaseManager) { }
/// }
/// 
/// // Register in startup
/// services.AddDatabaseManager(options =>
/// {
///     options.MapLibrary&lt;IContentLibraryMarker&gt;().UseSqlServer(connectionString);
/// });
/// </code>
/// </summary>
public interface IDatabaseManagerMarker
{
    // Empty interface - serves purely as a marker and type constraint
}
