# @fluentcms/client

A minimal typed client for [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET: Delivery reads, content management, typed filters, paging and the problems the API answers. It is ES modules over `fetch`, with no runtime dependency.

`fluentcms generate typescript --url https://cms.example.com --output src/cms` writes the types and field builders of an instance's schema, and that module imports this package:

```ts
import { ArticleFields, createClient } from "./cms/fluentcms.js";

const cms = createClient({ baseUrl: "https://cms.example.com/" });
```

`FluentCMS.Client` on nuget.org is the same client for .NET.
