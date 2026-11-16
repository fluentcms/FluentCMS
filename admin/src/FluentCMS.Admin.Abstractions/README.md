# FluentCMS.Admin.Abstractions

The contract of an admin half of [FluentCMS](https://github.com/fluentcms/FluentCMS), a headless CMS for .NET: the manifest, the contribution attributes, the base classes of field editors, list cells and actions, and the services and records that an admin component may use. A plugin's admin half compiles against it and `FluentCMS.Admin.Components` only.

It follows semantic versioning from 1.0.0 on, tracked by the public API analyzer and package validation, as `FluentCMS.Sdk` is. Previews may break it between previews. This preview holds no type yet; section 5.1 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
