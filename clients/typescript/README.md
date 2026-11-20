# @fluentcms/client

A minimal typed client for [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET: Delivery reads, content management, typed filters, paging and the problems the API answers. It is ES modules over `fetch`, with no runtime dependency.

`fluentcms generate typescript --url https://cms.example.com --output src/cms` writes the types and field builders of an instance's schema, and that module imports this package:

```ts
import { ArticleFields, createClient } from "./cms/fluentcms.js";

const cms = createClient({ baseUrl: "https://cms.example.com/" });
```

An answer that is not a success throws `FluentCmsProblem` with the status and, for a problem of the API, its `code`. A success whose body is not JSON, such as a page of a proxy, throws it too, with no code and the detail `The answer is not JSON.`, and so does an empty answer, a 204 or `null` where the call returns a result; only `delete` answers `undefined`. A 429 or a 503 has the server's `Retry-After`, in seconds, as `retryAfter`. A `Retry-After` that is a date is read against the answer's `Date`, which a browser hides from a page on another origin, since CORS does not expose it; there `retryAfter` is known only for the seconds the API sends. A segment of the path that is empty, `.` or `..` throws `TypeError`.

Every call takes a `signal` in its last argument, as in `cms.article.list(query, { signal })` or `cms.article.update(id, entry, version, { locale, signal })`, and `timeoutMs` in the options of the client aborts each request that takes longer. Either aborts the request and the read of its answer, and the call rejects with the signal's reason, or a `TimeoutError`.

`FluentCMS.Client` on nuget.org is the same client for .NET.
