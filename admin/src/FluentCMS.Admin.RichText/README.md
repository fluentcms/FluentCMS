# FluentCMS.Admin.RichText

The rich text editor of the [FluentCMS](https://github.com/fluentcms/FluentCMS) admin, which an app loads lazily the first time a form has a rich text field: Tiptap through JavaScript interop, on the server's rich text model. Section 4.7 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design, and the [admin overview](https://github.com/fluentcms/FluentCMS/blob/main/docs/admin.md) the guides.

## The bundle

`wwwroot/rich-text.js` is built from `js/` and committed, so building the admin needs no Node. After a change to `js/src/` or to a package, rebuild it and commit the result:

```sh
cd js
npm ci
npm test
npm run build
```

CI rebuilds it the same way and fails when the committed file differs.
