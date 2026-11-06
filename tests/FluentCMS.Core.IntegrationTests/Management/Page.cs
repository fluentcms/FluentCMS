using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Management;

// A localized type with a shared field, a relation and a component, for writes, versions per locale and publish warnings.
[Collection("page", Localized = true)]
public sealed class Page : ContentItem
{
    [Text(MaxLength = 200), Required, Localizable]
    public string Title { get; set; } = "";

    [Text(MaxLength = 50)]
    public string? Section { get; set; }

    [Relation]
    public Ref<Person> Owner { get; set; }

    [Component]
    public PageMeta Meta { get; set; } = new();
}

[Component]
public sealed class PageMeta
{
    [Text(MaxLength = 70)]
    public string? Heading { get; set; }

    [Text(MaxLength = 160)]
    public string? Description { get; set; }
}

[Collection("person")]
public sealed class Person : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Name { get; set; } = "";
}
