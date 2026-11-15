# @fluentcms/client

A minimal typed client for [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET: Delivery reads, content management, typed filters, paging and the problems the API answers. It is ES modules over `fetch`, with no runtime dependency.

`fluentcms generate typescript --url https://cms.example.com --output src/cms` writes the types and field builders of an instance's schema, and that module imports this package:

```ts
import { ArticleFields, createClient } from "./cms/fluentcms.js";

const cms = createClient({ baseUrl: "https://cms.example.com/" });
```

An answer that is not a success throws `FluentCmsProblem` with the status and, for a problem of the API, its `code`. A success whose body is not JSON, such as a page of a proxy, throws it too, with no code and the detail `The answer is not JSON.`, and an empty success answers `undefined`.

`FluentCMS.Client` on nuget.org is the same client for .NET.
