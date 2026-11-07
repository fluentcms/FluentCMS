# FluentCMS.Plugins.Webhooks

Signed deliveries of content and cache events to the webhooks of a site, with retries and a delivery log. The webhooks are managed under `/api/management/v1/webhooks` with the permission `webhooks.manage`. Section 5.2 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design.

## Settings

The options are in `FluentCms:Webhooks`:

| Key | Default | Meaning |
| --- | --- | --- |
| `AllowPrivateNetworks` | `false` | Lets deliveries reach loopback, private, link-local and the other addresses that are not public. Turn it on only when every receiver is internal. |
| `Timeout` | `00:00:10` | How long an attempt waits for the receiver's answer, at most five minutes. |
| `Retention` | `30.00:00:00` | How long the delivery log keeps an attempt. |
| `Workers` | `2` | Workers of the `webhooks` job queue on this instance. 0 runs no deliveries here. |
| `SecretOverlap` | `1.00:00:00` | How long the secret a rotation replaces keeps signing beside the new one. `00:00:00` ends it at the rotation. |

## The webhooks queue

Every attempt runs on the job queue `webhooks`: the first attempt of a delivery, its retries at 1m, 5m, 30m, 2h and 24h, a redelivery and a ping. A receiver that hangs holds the workers of this queue for its timeout, and scheduled publishing and the other jobs of the `default` queue keep running. `FluentCms:Jobs:Queues:webhooks` sets the workers too, and wins over `Workers`.

One attempt of a webhook is in flight at a time, on every instance together. A job claims its attempt by writing the attempt's row as `pending` before it sends, and a job that finds another attempt of its webhook pending waits until that attempt's send must have ended, at least a few seconds, and tries again. So a slow receiver delays its own deliveries and nobody else's.

A claim that is still pending a minute after the timeout has passed is abandoned, because its worker stopped before it recorded the result. The next job to find it marks the attempt `failed` and schedules the next attempt, without sending this one again. Since the receiver may have got the abandoned attempt, it can get a delivery more than once, and ignores a repeat by its `X-FluentCMS-Delivery` header.

## Secrets and headers

The secret is the caller's, or generated and returned by that create call only. The values of a webhook's headers are secrets too, since a receiver's credentials, such as `Authorization`, are what they are for. Both are stored protected with Data Protection. No answer holds a header's value, the one to a create included: each header is listed by name with the value `***`. Sending `***` back in a `PUT` keeps the value stored under that name, so a webhook read and sent back keeps its headers. Deliveries send the real values.

Webhooks saved before the headers were protected hold them in plain JSON. They are still delivered, and the plugin protects them once the host has started, or when the webhook is next saved.

`PUT` with a new `secret` rotates it. For `SecretOverlap`, 24 hours by default, the secret it replaces still signs every delivery beside the new one, and the answers carry `previousSecretExpiresAt`. Then only the new secret signs. Change the receiver over to the new secret within that time.

## The body

`{ version, id, event, occurredAt, site, origin, actor, data, links }`, where `version` is `1`. It changes only with a change that a receiver of version 1 could misread, and a new field is not one. A redelivery sends the body exactly as it was first sent.

## Receivers

A target URL uses `https` outside Development. Deliveries do not follow redirects and do not go through a system proxy. Every delivery carries:

- `X-FluentCMS-Event`, the event's name;
- `X-FluentCMS-Delivery`, the delivery id, the same for every attempt of a delivery;
- `X-FluentCMS-Signature: t=<unix>,v1=<hex>`, with one `v1` for each secret that signs: one, or two while a rotated secret overlaps, the new one first.

A receiver:

1. recomputes HMAC-SHA256 with its secret over `t + "." + body`, the body as received, before parsing it;
2. compares the result with every `v1` in constant time, and accepts the delivery when any of them matches;
3. refuses a `t` more than five minutes from its own clock, so a captured delivery cannot be replayed later;
4. answers 2xx quickly, and ignores a delivery whose `X-FluentCMS-Delivery` it has already handled, since a retry or an attempt whose result was lost sends it again.

In C#:

```csharp
static bool Verifies(string signature, string secret, string body, DateTimeOffset now)
{
    var parts = signature.Split(',').Select(part => part.Split('=', 2)).Where(part => part.Length == 2).ToList();
    if (parts.FirstOrDefault(part => part[0] == "t")?[1] is not { } t
        || !long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
        || Math.Abs(now.ToUnixTimeSeconds() - seconds) > 300)
    {
        return false;
    }

    var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(t + "." + body));
    return parts.Where(part => part[0] == "v1").Any(part =>
        part[1].Length == expected.Length * 2 && CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(part[1])));
}
```
