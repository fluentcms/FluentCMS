# FluentCMS.Plugins.Media.AzureBlob

Media storage for the media plugin of [FluentCMS](https://github.com/fluentcms/FluentCMS) on Azure Blob Storage, in a private container: new uploads go to the container, and files are served through the plugin's signed URLs.

```csharp
cms.AddPlugin<MediaPlugin>(media => media.UseAzureBlob(builder.Configuration.GetSection("AzureBlob")));
```

[The media plugin](https://github.com/fluentcms/FluentCMS/blob/main/src/plugins/FluentCMS.Plugins.Media/README.md#storage-on-s3-or-azure-blob) describes the keys of the section.
