namespace FluentCMS.Core.ChangeSets;

// What a change set operation can touch (CHG-01). Schema means a content type; the other kinds arrive with their epics.
public enum ItemKind
{
    Entry,
    Entity,
    Media,
    Schema,
    Rule,
}
