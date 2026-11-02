namespace FluentCMS.Core.Authorization;

// What a token or key may be used for, on top of what its roles grant. An API key carries its class as a scope
// (spec 4.7), which is where Delivery and Preview come from. The authentication handlers of M2 write these claims.
public static class TokenScopes
{
    // Space-separated, as in OAuth.
    public const string Claim = "scope";

    public const string Draft = "draft";
    public const string Management = "management";
    public const string Mcp = "mcp";
    public const string Delivery = "delivery";
    public const string Preview = "preview";
}
