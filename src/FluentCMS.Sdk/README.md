# FluentCMS.Sdk

The public contract of [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET: the interfaces, attributes, base classes, DTOs, events and permission names that plugins and host code compile against. It holds no implementation.

Content types are classes marked `[Collection]`, `[Singleton]`, `[Component]` or `[Block]`, with their fields as properties, and a plugin is a class marked `[FluentCmsPlugin]`. The engine that runs them is `FluentCMS.Core`.

The SDK follows semantic versioning from 1.0.0 on: its public surface is tracked by the public API analyzer and package validation, and a removal is announced with `[Obsolete]` a minor version ahead. Previews may break it between previews. See [releasing](https://github.com/fluentcms/FluentCMS/blob/main/docs/releasing.md).
