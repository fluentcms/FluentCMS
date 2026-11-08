namespace FluentCMS.Schema;

/// <summary>Marks the field for full-text search. Core only stores and describes it; the search index reads it (<see cref="Providers.ISearchIndex"/>).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SearchableAttribute : Attribute;
