namespace FluentCMS.Publishing;

/// <summary>What publishing an entry returns.</summary>
/// <typeparam name="T">The type of an entry.</typeparam>
/// <param name="Entry">The entry with its system fields, without what the principal may not read.</param>
/// <param name="Warnings">What a reader of the entry will miss. It says nothing about a target of a type the principal may not read, or about a field it may not read.</param>
public sealed record PublishResult<T>(T Entry, IReadOnlyList<PublishWarning> Warnings);
