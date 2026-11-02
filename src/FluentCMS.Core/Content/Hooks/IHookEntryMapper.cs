using FluentCMS.Content;

namespace FluentCMS.Core.Content.Hooks;

// Converts between the ContentDocument the content service works on and the content class a typed hook receives.
// The content service provides it; the pipeline only needs it when a hook is registered for a content class.
public interface IHookEntryMapper
{
    object ToEntry(Type clrType, ContentDocument document);

    // Writes the values of the entry back into the document and leaves the keys the class does not map alone.
    void CopyTo(object entry, ContentDocument document);
}
