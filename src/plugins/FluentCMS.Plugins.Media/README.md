# FluentCMS.Plugins.Media

Assets and folders for the sites of an instance: uploads whose type is read from their bytes, metadata with alternative text and captions per locale, file storage on the local disk, signed image transforms with stored variants and presets, usage, replace, private downloads, and the `media` field type for content. The media is managed under `/api/management/v1/media`, clients read it at `/api/delivery/v1/media/{id}`, images are served at `/media/{id}/{fileName}` and original files at `/media/{id}/original/{fileName}`. Section 5.1 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design.

## Options

The instance's options are in `FluentCms:Media`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Library` | `Shared` | `Shared`: every site reads and writes one library, that of the default site. `PerSite`: each site keeps its own assets, folders and presets (see below). |
| `Local:Root` | `App_Data/media` | The directory of the local storage, under the content root unless it is absolute. |
| `MaxRequestBodySize` | `268435456` | The largest upload request, in bytes. A site's `MaxRequestSize` above it is cut to it. |
| `RequireSignedUrls` | `true` | `false` serves unsigned transform URLs, for a setup where every client is trusted. The startup report lists `media-unsigned-urls` in Production. |
| `SigningKey` | none | The base64 of at least 32 bytes that signs transform URLs. Left out, the plugin generates one key (see below). |
| `CacheControl` | `public, max-age=86400` | The `Cache-Control` of a transformed image and of the original file of a public asset. |
| `DeletedRetention` | `30.00:00:00` | How long a deleted asset keeps its file and its row before the daily cleanup purges them. |

## One library or one per site

With `Library` set to `Shared`, the default, the sites of an instance share one media library: an asset, a folder or a preset made in one site is the same in every other, and its `siteId` is the default site's. The permission is still checked in the site of the request, with its folder limits, so each site's roles decide who uploads and who reads there, and the media settings below are still each site's. The usage of an asset lists the entries of every site that refer to it, and a delete is refused while any does.

With `PerSite`, each site keeps its own library, and an asset of one site is unknown in another. An instance with one site behaves the same either way. An instance that already has several sites, each with assets of its own, sets `PerSite` to keep reading them: with `Shared`, only the library of the default site is read. The option adds no table and moves no row, so it can be changed back.

A host that turns `PerSite` back to `Shared` hides the assets its sites uploaded while each had its own library: they are not found, and entries that refer to them show missing media. Nothing is moved or deleted. At start, with `Shared`, the plugin looks for assets of a site other than the default, and when it finds some, the startup report lists `media-shared-library-hides-sites` (a warning with event id 9131 in the log, and an entry of `insecureDefaults` in `fluentcms doctor` and `GET /api/management/v1/system/diagnostics`). It names `FluentCms:Media:Library=PerSite` and up to five of those sites, by id and name, with "and more" when there are others. Set `PerSite` again to read each site's own library; the plugin does not move assets into the shared library. With `PerSite`, with one site, or when no other site has an asset, the report says nothing. The check is one query that reads none of the library's own rows, so a large library does not slow the start; if it cannot run, the log says so (event id 9273) and the report says nothing of it.

## Site settings

The rules of uploads are each site's, in the settings area `media` (`settings.media.manage`):

| Setting | Default | Meaning |
| --- | --- | --- |
| `maxFileSize` | 50 MB | The largest file, in bytes. |
| `maxRequestSize` | 200 MB | The largest total of the files of one request, in bytes. |
| `maxFilesPerRequest` | 20 | The most files in one request. |
| `allowedTypes` | every type below | The types a site accepts: `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `image/avif`, `application/pdf`, `video/mp4`, `video/webm`, `audio/mpeg` and `audio/wav`. |
| `svg` | `refuse` | `refuse` refuses every SVG; `sanitize` stores it without its scripts, event handlers, `foreignObject` and `javascript:` URLs. |
| `maxTransformWidth` | 4096 | The largest width a transform asks for, in pixels. |
| `maxTransformHeight` | 4096 | The largest height a transform asks for, in pixels. |
| `maxTransformOperations` | 6 | The most parameters a transform has, its preset's included; `fx` and `fy` count as one. |
| `maxSourcePixels` | 50000000 | The most pixels an image may have to be transformed, every frame of an animation counted, read from its header before it is decoded. |
| `downloadUrlSeconds` | 300 | How long a download URL works, from 10 seconds to a day. |

A type comes from the first bytes of the file, never from its name or `Content-Type`. Executables are always refused, by their signature (Windows, ELF, Mach-O, a shebang script) and by an executable or script extension.

## Local storage in Production

The local disk is allowed in Production, and the startup report lists `media-local-storage` there. The files live on the machine of the instance, so:

- every instance of a deployment needs `Local:Root` on one shared volume;
- backups must include that directory, beside the database;
- a container needs the directory on a volume, or the files go with the container.

A storage on S3 or Azure Blob (below) replaces the local disk for new uploads.

## Storage on S3 or Azure Blob

`FluentCMS.Plugins.Media.S3` and `FluentCMS.Plugins.Media.AzureBlob` put a bucket or a container in the provider slot `IFileStorage`, where new uploads go. Their options are read from the section given and checked at start:

```csharp
using FluentCMS.Plugins.Media.S3;

cms.AddPlugin<MediaPlugin>(media => media.UseS3(builder.Configuration.GetSection("S3")));
```

| S3 key | Meaning |
| --- | --- |
| `Bucket` | The bucket. Required. |
| `Endpoint` | An S3-compatible service, such as `http://minio:9000`. Left out, AWS in the region. |
| `Region` | The AWS region. Left out, the AWS SDK's own (`AWS_REGION` or a profile); with `Endpoint`, the region requests are signed for, `us-east-1` by default. |
| `KeyPrefix` | Put before every key, such as `media/`. |
| `ForcePathStyle` | `true` addresses the bucket in the path, as most S3-compatible services need. |
| `AccessKeyId`, `SecretAccessKey`, `SessionToken` | The access key, both parts or neither. Left out, the AWS SDK's default chain: the environment, a profile, or the role of the instance or task. |

The credentials need `s3:GetObject`, `s3:PutObject`, `s3:DeleteObject` and `s3:ListBucket` on the bucket.

```csharp
using FluentCMS.Plugins.Media.AzureBlob;

cms.AddPlugin<MediaPlugin>(media => media.UseAzureBlob(builder.Configuration.GetSection("AzureBlob")));
```

| Azure Blob key | Meaning |
| --- | --- |
| `ConnectionString`, `Container` | The connection string of the account and the name of the container. |
| `ContainerUrl` | Instead of both: `https://{account}.blob.core.windows.net/{container}?{sas}`, with a SAS that grants read, write, delete and list. |
| `KeyPrefix` | Put before every key, such as `media/`. |

Managed identity is not supported, since it needs `Azure.Identity`.

Objects stay private: keep the bucket's public access blocked and the container's access level private. Bytes leave through `/media` and signed URLs only, never from the bucket or container itself. A variant is stored beside its asset, in the same bucket or container.

Each asset records the storage it was written with. An asset written on the local disk is still served, downloaded and transformed from `Local:Root` after the move, and its variants are written there; replacing it writes the new file to the new storage and deletes the old one from the disk. Keep that directory until its assets are gone. Moving from one cloud storage to another with older assets left behind is not covered.

`/health/ready` includes the check `media-storage` of the storage in the slot: the local disk must take a file in its root, S3 must answer a `HEAD` of the bucket, and Azure Blob must list the container. It is Unhealthy when the bucket or container cannot be reached. A failure is described by its status and error code only, so no log line, health answer or startup report holds a credential.

## Permissions

`media.read`, `media.upload`, `media.update`, `media.delete`, `media.folders.manage` and `media.presets.manage`. A grant can be limited to folders with the `resources` constraint, listing folder ids; it then reaches those folders and their subfolders only, and not the root:

```json
{ "permission": "media.*", "constraints": { "resources": ["0193a0c2-5f4e-7c1d-9a3b-2b8f6e1d4c70"] } }
```

Presets are the site's, so only a grant of `media.presets.manage` without a folder limit changes them.

## Upload

`POST /api/management/v1/media` with a `multipart/form-data` body. Every file part is a file of the upload, and the field `folderId` names the folder, or leave it out for the root. A request stores all its files or none, and answers 201 with the new assets:

```bash
curl -H "Authorization: ApiKey $KEY" -F folderId=$FOLDER -F files=@photo.jpg -F files=@report.pdf https://cms.example.com/api/management/v1/media
```

## Image transforms

`GET /media/{id}/{fileName}` serves an image of a public asset, resized and encoded as its query asks. It is open to anyone: the signature is the authority. The file name is there for the reader and is ignored, so `/media/{id}/hero.jpg` and `/media/{id}/x.webp` are the same image. A private or deleted asset is never served, even with a valid signature.

| Parameter | Meaning |
| --- | --- |
| `w`, `h` | The width and height in pixels. With one of them, the other follows the aspect ratio. |
| `fit` | With both: `contain` (the default) fits the image inside, `cover` fills them and crops around the focal point, `fill` stretches. |
| `q` | The quality of JPEG and WebP, from 1 to 100; 80 by default. |
| `f` | `auto`, `webp`, `avif`, `jpeg` or `png`; the asset's own type when left out. `auto` picks WebP when `Accept` lists it, AVIF only where the processor encodes it, and the asset's type otherwise, and answers with `Vary: Accept`. The default processor encodes no AVIF. |
| `fx`, `fy` | The focal point of a `cover`, from 0 to 1, given together; the asset's own focal point, or its centre, by default. |
| `preset` | A preset of the site, whose parameters apply under those of the URL. |
| `sig` | The signature. |

A request over the limits of the site's media settings is refused with `FCMS1591` before the image is read. An image over `maxSourcePixels`, or whose side that follows the aspect ratio would be over the width or height limit, is refused from its header before it is decoded. Every result is stored as a variant in the asset's storage, so the same request, under any file name, is served from storage. The orientation of a photo is applied, and its EXIF, IPTC and XMP metadata are left out. A daily job, `media.variants.cleanup`, removes the variants of deleted assets.

Host code and other plugins build a URL with `IMediaService.GetImageUrlAsync`:

```csharp
var url = await media.GetImageUrlAsync(asset, new MediaImageOptions { Width = 1200, Height = 630, Fit = ImageFit.Cover, Format = MediaImageFormat.Auto });
// /media/0193a0c2-.../hero.jpg?w=1200&h=630&fit=cover&f=auto&sig=...
```

## Delivery API

`GET /api/delivery/v1/media/{id}` answers an asset's metadata, with `alt` and `caption` in the request's locale, and its URLs, relative to the base of the CMS:

```json
{
  "id": "0193a0c2-...", "fileName": "hero.jpg", "mimeType": "image/jpeg", "size": 48213, "width": 1600, "height": 900,
  "title": "Harbour", "locale": "en", "alt": "Boats in a harbour", "caption": null, "credit": null, "tags": [],
  "focalPoint": { "x": 0.5, "y": 0.4 }, "isPrivate": false, "createdAt": "...", "updatedAt": "...",
  "urls": {
    "original": "/media/0193a0c2-.../original/hero.jpg?sig=...",
    "presets": { "hero": "/media/0193a0c2-.../hero.jpg?preset=hero&sig=..." },
    "template": null
  }
}
```

The URLs are signed. `presets` has one transform URL for each preset of the site, for an image the processor reads, and `template`, only where `RequireSignedUrls` is `false`, is a transform URL whose `{w}`, `{h}`, `{fit}`, `{q}` and `{f}` a client fills in or leaves out. The answer has the cache headers of content: an `ETag`, the `Cache-Control` of `FluentCms:Delivery:Cache`, and a copy in the output cache that a change to the asset or to the site's presets evicts. A private asset is answered, without URLs, only to a caller whose `media.read` reaches its folder; the anonymous caller and Delivery keys hold no permission but reading content, so they never read one.

## Usage, delete and replace

`GET /api/management/v1/media/{id}/usage` lists the entries whose fields, blocks or rich text refer to the asset, as `{ "items": [{ "type", "entryId", "status", "locale", "field" }] }`, without the entries, locales and fields the caller cannot read. Entries in the trash are left out.

`DELETE /api/management/v1/media/{id}` refuses an asset that entries refer to (`FCMS1594`, 409). `?force=true` deletes it anyway and removes the references, so the fields that held it read as empty. A deleted asset keeps its file until `DeletedRetention` has passed, when the daily cleanup purges the file and the row.

`POST /api/management/v1/media/{id}/replace` with a `multipart/form-data` body of one file replaces the file of an asset (`media.update`). The file passes the checks of an upload. The id, the metadata and the references stay, the variants of the old file are removed, and `media.replaced` is raised.

## Original files and private downloads

`GET /media/{id}/original/{fileName}` sends a file as it was stored, with `X-Content-Type-Options: nosniff`, and an SVG with a `Content-Security-Policy` that runs nothing. A file whose stored type is none the plugin reads from bytes is sent as `application/octet-stream`, as an attachment. Without `exp`, it serves a public asset with a signature that the Delivery API's `urls.original` carries. A private asset is served only by a download URL:

```bash
curl -X POST -H "Authorization: ApiKey $KEY" https://cms.example.com/api/management/v1/media/$ID/download-url
# { "url": "/media/.../original/plan.pdf?exp=1762600000&sig=...", "expiresAt": "..." }
```

It needs `media.read` in the asset's folder. The URL serves the file as an attachment that no cache keeps until `downloadUrlSeconds` have passed, and is refused after that (`FCMS1595`, 403).

## Presets

`PUT /api/management/v1/media/presets/{name}` creates or replaces a preset of the site, `GET /api/management/v1/media/presets` lists them and `DELETE` removes one. A name is lower-case letters, digits and hyphens. The body has the parameters by their full names:

```bash
curl -X PUT -H "Authorization: ApiKey $KEY" -H "Content-Type: application/json" -d '{"width":1200,"height":630,"fit":"cover","format":"auto"}' https://cms.example.com/api/management/v1/media/presets/hero
```

A signed URL with `preset=hero` then serves that size.

## Signed URLs

A signature is the HMAC-SHA256 of the asset id and the URL's parameters, sorted by name, without `sig`, `site` and the file name. The key is `FluentCms:Media:SigningKey` when it is set. Otherwise the plugin generates one 256-bit key at its first start, and stores it in `cms_media_signing_key`, protected by Data Protection. Instances that start together share one key, created under a lock. Data Protection protects the key only at rest, so signed URLs keep working when its key ring rotates.

Download URLs and the URLs of original files are signed with the same key. Replacing the key invalidates every URL signed before, in content and in CDN caches. To replace it, set `SigningKey`, or delete the row of `cms_media_signing_key`, and restart every instance. Keep the Data Protection key ring in backups: without the key that protected the signing key, the instance cannot read it, and only setting `SigningKey` or deleting the row brings transforms back.

## Media fields

The plugin adds the `media` field type. A content class marks a `MediaRef?` or a `MediaRefList` with `[Media]`, and a schema file uses `"type": "media"`, with `"multiple": true` for a list:

```csharp
[Media]
public MediaRef? Hero { get; set; }

[Media]
public MediaRefList Gallery { get; set; } = [];
```

A write sends asset ids and refuses one that is no asset of the site, or is deleted, with `FCMS2113`. A filter finds entries by asset id with `eq` and `in`, such as `filter[hero][eq]={id}`. A read gives `{ "id", "type": "media" }`, and `populate=hero` gives the asset: its file name, type, size, width and height, title, alternative text and caption in the locale of the read, credit, focal point, and `urls` as the Delivery API gives them (`original`, `presets` and, where unsigned URLs are served, `template`). `populate=sections` does the same for the media of a block list or a repeated component, and `populate=*` for all of them. On the Delivery API a public asset is populated for whoever reads, the Public role, a Delivery key and a preview token included, with the URLs `GET /api/delivery/v1/media/{id}` gives it, and a private asset, a deleted one and one of another site's library are null, as is every asset in a site where the plugin is disabled. So it is on any other surface, GraphQL for one, for a caller without the `management` scope. On the Management API, and elsewhere for a caller with the scope, the caller needs `media.read`, within its folders, to see an asset populated; one it cannot read is null, and a private asset has `urls` null. A Delivery read that shows an asset is evicted from the output cache with the asset's own Delivery copy, when the asset changes, turns private or is deleted. With `richText=html` or `markdown`, a media node of rich text is written with the URL of the asset's original file and, when the node has no `alt`, the asset's alternative text, for the assets the same rule resolves; any other is written without a URL. Other plugins read assets the same way through `IMediaResolver`.

Without the plugin, a type with a media field stops the host with `FCMS1203`.

## SkiaSharp

The default `IImageProcessor` uses [SkiaSharp](https://github.com/mono/SkiaSharp), under the MIT License, with `SkiaSharp.NativeAssets.Linux.NoDependencies`, so that it runs on Linux and in containers without fontconfig. It reads JPEG, PNG, GIF (its first frame) and WebP, and writes JPEG, PNG and WebP; it writes no AVIF and no GIF, so `f=avif` is refused and a GIF without `f` is served as a PNG. ImageSharp was not chosen: version 4 needs a Six Labors license key to build in Release, and the last version without that check has open advisories. A host that wants another processor, ImageSharp under its own Six Labors license included, replaces the provider slot:

```csharp
builder.Services.AddFluentCms(cms => cms.Providers.Use<IImageProcessor, MyImageProcessor>());
```
