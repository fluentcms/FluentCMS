# 0004 Limit a grant to media folders with a resources constraint that the plugin evaluates

## Status

Accepted.

## Date

2026-11-08

## Context

[#2832](https://github.com/fluentcms/FluentCMS/issues/2832) needs a grant of a media permission (`media.read`, `media.upload`, `media.update`, `media.delete`, `media.folders.manage`) that reaches some folders and their subfolders only. Spec 5.1 names a permission `media.folder.{id}.read`, but the permission catalogue is built once at startup from core and the plugins' `IPermissionProvider`s (spec 4.8), and folders are created at runtime, so the catalogue cannot hold a permission per folder. Grants are additive: a constraint only narrows, and one this version does not know fails rather than being ignored, because ignoring it widens the grant. Core must not know what a media folder is (spec 2.1).

Options considered:

- **A. A permission per folder**, added to the catalogue as folders are created. The catalogue stops being fixed at startup, and every instance must rebuild it when another creates a folder.
- **B. A table of the plugin** that maps roles to folders. A plugin cannot see a principal's roles through the SDK, and a mapping that narrows a role's `media.*` is a deny rule, which the model does not have.
- **C. A grant constraint that names the plugin's resources**, stored with the other constraints in `cms_role_permission.constraints` and answered by `IPermissionChecker`, with the plugin deciding what an id means and how far it reaches.

## Decision

We will adopt option C:

- **Constraint.** `PermissionConstraints` gets `resources`, a list of ids (1 to 100, each up to 128 characters) that core stores and compares as text, beside `sites`, `locales`, `fields` and `rowRule`. Role management validates the shape only; an id that names nothing reaches nothing.
- **Checks.** A grant with `resources` never counts in a check that cannot say which resource it acts on: core's checks, content checks, and `IPermissionChecker.AllowsAsync` and `RequireAsync`. So `*` limited to resources gives no core or content permission. The new `IPermissionChecker.ResourcesAsync(permission, site)` answers null when a grant with no resource limit holds the permission, otherwise the union of the resources of the grants that hold it, which is empty when none does.
- **Handing out.** A caller covers a grant with resources only through a grant of its own with no resource limit, or with a superset of those resources, so no one hands out more than they hold.
- **Media.** The ids are folder ids. The media service reaches the named folders of the site and every folder whose path starts with theirs, so a grant follows a folder that moves. The root is reached only by a grant with no resource limit. An asset or folder outside the reach is not found; no grant at all is a 403.

## Consequences

- The catalogue stays fixed at startup, and folder limits need no schema change of core: the constraint is JSON in the existing column.
- Any plugin with resources of its own can use the same constraint and check.
- A grant limited to a folder that is deleted reaches nothing until the role is edited; core cannot clean it up, since it does not know folders.
- A caller limited to a folder cannot hand out one of its subfolders on its own, because core compares ids and not the tree. A caller with no folder limit can.
- The private-asset download of [#2834](https://github.com/fluentcms/FluentCMS/issues/2834) checks `media.read` through the same reach instead of `media.folder.{id}.read`.
