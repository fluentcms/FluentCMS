# FluentCMS.Client

A minimal typed HTTP client for [FluentCMS](https://github.com/fluentcms/FluentCMS): Delivery reads, content management, typed filters, paging and the problems the API answers, as `FluentCmsProblemException`. It references no other FluentCMS package, so an application in another process does not get the host's types.

An answer that is not a success throws `FluentCmsProblemException` with the status and, for a problem of the API, its `Code`. A success whose body is not JSON, such as a page of a proxy, throws it too, with no code and the detail `The answer is not JSON.`, and so does an empty answer, a 204 or `null` where the call returns a result; only a delete returns nothing. A 429 or a 503 has the server's `Retry-After` as `RetryAfter`. A segment of the path that is empty, `.` or `..` throws `ArgumentException`.

`fluentcms generate csharp --url https://cms.example.com` writes the entry classes and field builders of an instance's schema, and that code compiles against this package. `@fluentcms/client` is the same client for TypeScript.
