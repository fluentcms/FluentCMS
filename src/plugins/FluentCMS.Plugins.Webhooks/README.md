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

## The webhooks queue

Every attempt runs on the job queue `webhooks`: the first attempt of a delivery, its retries at 1m, 5m, 30m, 2h and 24h, a redelivery and a ping. A receiver that hangs holds the workers of this queue for its timeout, and scheduled publishing and the other jobs of the `default` queue keep running. `FluentCms:Jobs:Queues:webhooks` sets the workers too, and wins over `Workers`.

One attempt of a webhook is in flight at a time, on every instance together. A job claims its attempt by writing the attempt's row as `pending` before it sends, and a job that finds another attempt of its webhook pending waits until that attempt's send must have ended, at least a few seconds, and tries again. So a slow receiver delays its own deliveries and nobody else's.

A claim that is still pending a minute after the timeout has passed is abandoned, because its worker stopped before it recorded the result. The next job to find it marks the attempt `failed` and schedules the next attempt, without sending this one again. Since the receiver may have got the abandoned attempt, it can get a delivery more than once, and ignores a repeat by its `X-FluentCMS-Delivery` header.

## Receivers

A target URL uses `https` outside Development. Deliveries do not follow redirects and do not go through a system proxy. A receiver checks `X-FluentCMS-Signature: t=<unix>,v1=<hex>` by computing HMAC-SHA256 with the webhook's secret over `t + "." + body`.
