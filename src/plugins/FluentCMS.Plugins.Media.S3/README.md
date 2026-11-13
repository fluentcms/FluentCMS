# FluentCMS.Plugins.Media.S3

Media storage for the media plugin of [FluentCMS](https://github.com/fluentcms/FluentCMS) on Amazon S3 and S3-compatible services such as MinIO, with private objects: new uploads go to the bucket, and files are served through the plugin's signed URLs.

```csharp
cms.AddPlugin<MediaPlugin>(media => media.UseS3(builder.Configuration.GetSection("S3")));
```

The section takes `Bucket`, `Endpoint`, `Region`, `KeyPrefix`, `ForcePathStyle`, `AccessKeyId`, `SecretAccessKey` and `SessionToken`; [the media plugin](https://github.com/fluentcms/FluentCMS/blob/main/src/plugins/FluentCMS.Plugins.Media/README.md#storage-on-s3-or-azure-blob) describes each.
