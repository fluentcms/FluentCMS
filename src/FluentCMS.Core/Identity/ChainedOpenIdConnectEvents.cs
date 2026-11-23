using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Identity;

// The events of an OpenID Connect provider of either pool, set after every Configure (#3225, #3526). What FluentCMS does
// on an event is an override of its method, which then calls the event's delegate; every delegate starts as the events the
// host set, as Events or as EventsType, so neither those nor a delegate the host sets later drops FluentCMS's part. A host
// that replaces the events themselves after they are set is stopped at start (Kept, #3531).
internal abstract class ChainedOpenIdConnectEvents : OpenIdConnectEvents
{
    private static readonly OpenIdConnectEvents NoEvents = new();

    private readonly object _hostItem = new();
    private readonly OpenIdConnectEvents? _host;
    private readonly Type? _hostType;

    protected ChainedOpenIdConnectEvents(OpenIdConnectOptions options)
    {
        (_host, _hostType) = (options.Events, options.EventsType);
        OnAuthenticationFailed = context => Host(context.HttpContext).AuthenticationFailed(context);
        OnAuthorizationCodeReceived = context => Host(context.HttpContext).AuthorizationCodeReceived(context);
        OnMessageReceived = context => Host(context.HttpContext).MessageReceived(context);
        OnPushAuthorization = context => Host(context.HttpContext).PushAuthorization(context);
        OnRedirectToIdentityProvider = context => Host(context.HttpContext).RedirectToIdentityProvider(context);
        OnRedirectToIdentityProviderForSignOut = context => Host(context.HttpContext).RedirectToIdentityProviderForSignOut(context);
        OnSignedOutCallbackRedirect = context => Host(context.HttpContext).SignedOutCallbackRedirect(context);
        OnRemoteSignOut = context => Host(context.HttpContext).RemoteSignOut(context);
        OnTokenResponseReceived = context => Host(context.HttpContext).TokenResponseReceived(context);
        OnTokenValidated = context => Host(context.HttpContext).TokenValidated(context);
        OnUserInformationReceived = context => Host(context.HttpContext).UserInformationReceived(context);
        OnAccessDenied = context => Host(context.HttpContext).AccessDenied(context);
        OnRemoteFailure = context => Host(context.HttpContext).RemoteFailure(context);
        OnTicketReceived = context => Host(context.HttpContext).TicketReceived(context);
    }

    // Whether the handler raises these events still, once every PostConfigure has run.
    public static bool Kept<TEvents>(OpenIdConnectOptions options)
        where TEvents : ChainedOpenIdConnectEvents =>
        options.Events is TEvents && options.EventsType is null;

    // A type is resolved once a request, as the handler resolves EventsType.
    private OpenIdConnectEvents Host(HttpContext http)
    {
        if (_hostType is null)
        {
            return _host ?? NoEvents;
        }

        if (http.Items.TryGetValue(_hostItem, out var resolved) && resolved is OpenIdConnectEvents events)
        {
            return events;
        }

        events = (OpenIdConnectEvents)http.RequestServices.GetRequiredService(_hostType);
        http.Items[_hostItem] = events;
        return events;
    }
}
