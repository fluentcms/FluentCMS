using FluentCMS.Schema;

namespace FluentCMS.Core.IntegrationTests.Members;

// Anyone reads these: the Public role of the default site grants it.
[Collection("bulletin")]
public sealed class Bulletin : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}

// Members read these, by the built-in Member role, and only those of their tier: the row rule reads the member's groups.
[Collection("perk")]
public sealed class Perk : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";

    [Text(MaxLength = 20), Required]
    public string Tier { get; set; } = "";
}

// Only the members of the insiders group read these, by the role the group is bound to.
[Collection("dossier")]
public sealed class Dossier : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}
