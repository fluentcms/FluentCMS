# FluentCMS.Plugins.AiAssist.Admin

The admin half of the [FluentCMS](https://github.com/fluentcms/FluentCMS) AI assist plugin, for an admin app built on `FluentCMS.Admin`. The app references it and loads it lazily, the first time one of its contributions is used:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Plugins.AiAssist.Admin" />
  <BlazorWebAssemblyLazyLoad Include="FluentCMS.Plugins.AiAssist.Admin.wasm" />
</ItemGroup>
```

The half adds the AI actions to the fields of the content form that the site's AI settings opt in (rewrite, shorten, expand, fix grammar, title, summary, SEO, translate into another locale of the site, whose draft takes the accepted translation, and alt text of an image of the entry's saved draft, offered only where the site's AI settings turn on `assetAltText` and to a caller who may update images, so not to the built-in Author), with the suggestion streamed into a panel and put into the draft only when it is accepted, the alt text of an image of a media field, offered on the same terms and saved on the image when it is accepted, and the page `/ai/usage`, which lists and totals the AI calls of the site. Section 5.5 of the [admin technical specification](https://github.com/fluentcms/FluentCMS/blob/main/docs/planning/admin-technical-spec.md) has the design.
