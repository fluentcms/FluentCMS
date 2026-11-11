# FluentCMS.Plugins.MultiSite

Sites beyond the default one, and the host names that decide the site of a request. The plugin adds its routes under `/api/management/v1/sites`, beside the routes of core that read and change a site and its locales. Sections 5.6 and 4.6 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) have the full design.

A host adds the plugin with `cms.AddPlugin<MultiSitePlugin>()`, or references the package and lets it be discovered. It has no options and no permission of its own.

## Sites

Every route needs `sites.manage` for the instance: a role of one site, a grant limited to sites and a key bound to sites do not hold it.

| Route | Body | Answer |
| --- | --- | --- |
| `POST /api/management/v1/sites` | `{ "name", "slug", "defaultLocale" }` | 201 with the site |
| `DELETE /api/management/v1/sites/{id}` | | 204 |

A new site has its default locale and its own Public role, which grants nothing until a role is given grants. A slug is lower case letters and digits joined by hyphens, and is unique in the instance (`FCMS2304`); the locale is a BCP 47 language tag (`FCMS2311`).

A delete refuses the default site (`FCMS2302`) and a site that still has content, trashed entries included (`FCMS2303`). It removes the site's locales, roles, settings, plugin rows and host names. The activity rows are `site.created`, in the new site, and `site.deleted`, which belongs to no site.

`GET /api/management/v1/me/sites`, a route of core, lists the sites in which the caller holds a grant, so an admin shows a user only its sites.

## Host names

| Route | Body | Answer |
| --- | --- | --- |
| `GET /api/management/v1/sites/{id}/hosts` | | `{ "items" }`, the primary host first |
| `POST /api/management/v1/sites/{id}/hosts` | `{ "hostname", "isPrimary" }` | 201 with the host |
| `PATCH /api/management/v1/sites/{id}/hosts/{hostname}` | `{ "isPrimary": true }` | the host |
| `DELETE /api/management/v1/sites/{id}/hosts/{hostname}` | | 204 |

A host is `{ "siteId", "hostname", "isPrimary", "createdAt" }`.

A host name is a DNS name, such as `www.example.com` or `localhost`. It is stored in lower case, and an internationalized name as its A-label, so `bücher.example` is `xn--bcher-kva.example`. It has at most 253 characters, in labels of 1 to 63 letters, digits and hyphens. A scheme, a path, a space or an IP address is refused (`FCMS1660`), and so are a wildcard (`FCMS1661`) and a port (`FCMS1662`): a site answers on every port of its host names, and each name it answers to is added on its own.

A host name belongs to one site in the whole instance. A name that a site already has is refused (`FCMS1663`, 409).

A site with host names has exactly one primary host. The first host of a site is its primary host, whatever `isPrimary` says. A later host becomes the primary one when it is added with `isPrimary: true` or patched to it, in place of the one that was. The primary host stays primary until another is made primary, and it is removed last (`FCMS1665`, 409). The primary host is data for a front end, such as the canonical name of the site. The backend redirects nothing by it.

Every change raises `SiteUpdatedEvent` for the site, so its Delivery copies are evicted, and writes the activity row `site.host-added`, `site.host-updated` or `site.host-removed`, which names the host.

## How a request finds its site

The first of these that names a site wins:

1. the binding of the API key;
2. the `X-Cms-Site` header;
3. the `site` parameter;
4. the host name of the request;
5. the default site.

So an explicit header or parameter wins over the host name, and an administrator on one host name can manage any site. A host name that no site has reads the default site. The Delivery API, the Management API, GraphQL and MCP all resolve the site this way.

The host name is the request's `Host`, without its port, in any case. Behind a reverse proxy it is the forwarded host only when `FluentCms:ReverseProxy:ForwardedHeaders:ForwardHost` is on and the host is one of `AllowedHosts` (see [the reverse proxy guide](../../../docs/reverse-proxy.md)). List every host name of every site in `AllowedHosts`, so that a request with any other `Host` is turned away.

The authorization server and the hosted pages stay on the public origin (`FluentCms:PublicOrigin`), with one issuer for every site. A token works on every host name.

Each instance keeps the map of host names in memory for 30 seconds. The instance that changes a host name reads the change at once, and another instance within 30 seconds. The Delivery output cache keeps the copies of a read apart by the site its host name resolved to.

## Media

Two sites share one media library unless `FluentCms:Media:Library` is `PerSite` (see the [media plugin](../FluentCMS.Plugins.Media/README.md)).

## Per-site enablement

Where the plugin is disabled for a site, its routes answer 404 (`FCMS1550`) for a request of that site. Host names are still resolved, because a host name is read before any site is known.

## Codes

| Code | Status | Meaning |
| --- | --- | --- |
| `FCMS1660` | 422 | Not a host name |
| `FCMS1661` | 422 | A wildcard |
| `FCMS1662` | 422 | A host name with a port |
| `FCMS1663` | 409 | A site has the host name |
| `FCMS1664` | 404 | The site has no such host name |
| `FCMS1665` | 409 | The primary host cannot be removed or demoted yet |

Log event ids 9340 to 9344.
