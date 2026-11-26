# FluentCMS.Plugins.Media

Assets and folders for the sites of an instance: uploads whose type is read from their bytes, metadata with alternative text and captions per locale, file storage on the local disk, signed image transforms with stored variants and presets, usage, replace, private downloads, and the `media` field type for content. The media is managed under `/api/management/v1/media`, clients read it at `/api/delivery/v1/media/{id}`, images are served at `/media/{id}/{fileName}`, original files at `/media/{id}/original/{fileName}` and the thumbnails of the media library at `/media/{id}/thumbnail/{fileName}`. Section 5.1 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design.

## Options

The instance's options are in `FluentCms:Media`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Library` | `Shared` | `Shared`: every site reads and writes one library, that of the default site. `PerSite`: each site keeps its own assets, folders and presets (see below). |
| `Local:Root` | `App_Data/media` | The directory of the local storage, under the content root unless it is absolute. |
| `MaxRequestBodySize` | `268435456` | The largest upload request, in bytes. A site's `MaxRequestSize` above it is cut to it. |
| `RequireSignedUrls` | `true` | `false` serves unsigned transform URLs, for a setup where every client is trusted. The startup report lists `media-unsigned-urls` outside Development. |
| `SigningKey` | none | The base64 of at least 32 bytes that signs transform URLs. Left out, the plugin generates one key (see below). |
| `CacheControl` | `public, max-age=86400` | The `Cache-Control` of a transformed image and of the original file and the thumbnail of a public asset. |
| `DeletedRetention` | `30.00:00:00` | How long a deleted asset keeps its file and its row before the daily cleanup purges them, as far as no other asset names the file (see the delete below). |
| `MaxConcurrentDecodes` | `4` | How many images the instance decodes at once to make variants, thumbnails and copies, from 1 to 256. A decode at the default `maxSourcePixels` may take about 200 MB; requests beyond the limit wait. |

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
| `svg` | `refuse` | `refuse` refuses every SVG; `sanitize` stores it with only the SVG elements and attributes of drawing it lists: no scripts, event handlers, `foreignObject`, images, `<style>` elements or HTML, no link or `url()` but to a `#fragment` of the file, and every id and every reference to one under a prefix of the file. |
| `maxSvgFileSize` | 1 MB | The largest SVG, in bytes. A file that starts as markup and is larger is refused as too large (`FCMS1582`) before it is parsed, whatever `svg` says. `maxFileSize` still holds when it is the smaller. The limit is measured on the file as received, the file in the package for an import; the stored drawing can be a little larger, since each id and reference grows by its prefix. `fluentcms media sanitize-svg` parses no stored SVG larger than it (below). |
| `maxTransformWidth` | 4096 | The largest width a transform asks for, in pixels. |
| `maxTransformHeight` | 4096 | The largest height a transform asks for, in pixels. |
| `maxTransformOperations` | 6 | The most parameters a transform has, its preset's included; `fx` and `fy` count as one. |
| `maxSourcePixels` | 50000000 | The most pixels an image may have to be transformed, every frame of an animation counted, read from its header before it is decoded. |
| `downloadUrlSeconds` | 300 | How long a download URL works, from 10 seconds to a day. |

A type comes from the first bytes of the file, never from its name or `Content-Type`. Executables are always refused, by their signature (Windows, ELF, Mach-O, a shebang script) and by an executable or script extension.

## Local storage in Production

The local disk is allowed outside Development, and the startup report lists `media-local-storage` there. The files live on the machine of the instance, so:

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

The first time the plugin starts on an instance, the built-in Editor gets `media.read`, `media.upload` and `media.update`, and the built-in Author gets `media.read` and `media.upload`, each with no constraints. Each default is given once, so a grant you take away later is not given back at the next start. A role that already holds a limited grant of any media permission, such as one limited to folders, gets none of these defaults.

## Upload

`POST /api/management/v1/media` with a `multipart/form-data` body. Every file part is a file of the upload, and the field `folderId` names the folder, or leave it out for the root. A request stores all its files or none, and answers 201 with the new assets:

```bash
curl -H "Authorization: ApiKey $KEY" -F folderId=$FOLDER -F files=@photo.jpg -F files=@report.pdf https://cms.example.com/api/management/v1/media
```

An image's `width` and `height` are those the image processor reads, the size it is shown at after its EXIF orientation. The job `media.dimensions.backfill` gives the JPEG, WebP and AVIF images stored before the same size once per database, starting at a start of an instance, and then records that it is done in the row with id 2 of `cms_media_signing_key`. An image uploaded by an instance of an earlier version that still runs during a rolling upgrade can keep the size of its header after that. To run the check again, delete the row with id 2 of `cms_media_signing_key` (`DELETE FROM cms_media_signing_key WHERE id = 2`) and start an instance.

With `svg` set to `sanitize`, a `<style>` element goes whatever it holds, since a page that inlines the SVG would apply its rules to the whole page; `style` attributes stay, but not on the root `<svg>`, and a drawing whose colours were in a `<style>` loses them. A `style` attribute or other value read as CSS that holds anything the plugin removes, such as one `url(data:…)` or an `element(…)`, goes whole. The root also loses `overflow`, `transform` and `filter`, and an animation of the root that sets one of them or its `width`, `height`, `x`, `y`, `viewBox` or `preserveAspectRatio`, or moves the root, goes. Every `id` takes the prefix `fcms_`, sixteen hex digits of the SHA-256 of the sanitised file as written before the prefix, and an underscore, and so does every reference to one (`href`, `url(#…)`, `aria-labelledby`, `aria-describedby`, and the `begin` and `end` of an animation), so that an inlined SVG puts no name of its own on `window`, clashes with no id of the page or of another SVG, and reaches no element of the page; a hyphen would be read as an offset in `begin="…a.end"`. A `begin` or `end` list with a value the timing grammar does not read whole goes, and so does one with `accessKey(…)` or `wallclock(…)`; an animation that loses its `begin` or `end`, to the grammar or to the lists, goes with it, rather than start when the drawing loads or never end. An id with a hyphen of its own, such as `begin="my-dot.end"`, does not time an animation in Chromium, Firefox or WebKit, before sanitising or after: the browser reads the hyphen as the start of an offset, so name such an element without one. A prefix of that form that the file carries is kept only when taking it off and scoping the file again gives the same bytes. The file is written with SVG as the default namespace and no prefix on any element, and without an XML declaration, CDATA section or comment, so that a page that parses it as HTML reads what an XML parser reads. A file that nests elements more than 128 levels deep, or holds more than 100,000 nodes (elements, text, the white space between elements included, CDATA sections, comments and processing instructions), or has an element with more than 1,000 attributes, namespace declarations included, is refused as no SVG (`FCMS1581`). The node cap is fixed and is not a setting, and an indented drawing spends some of it on its white space; `maxSvgFileSize` is what a site with large drawings raises. At the default of 1 MB, the costliest file, one element with as many attributes as fit, takes about half a second to refuse. The reader's pass checks for a cancelled request at each node and the passes over the tree at each element, but one element is a single read and the tree is built in one call, so the size limit, not the cancellation, bounds those.

A page that inlines an SVG puts it in a box that sets its size, hides what overflows (`overflow: hidden`) and contains it (`contain: paint`). That box is what keeps the drawing in its place: the sanitiser removes the ways out of it that are known, and does not promise there is no other. A `class` name on an inlined drawing picks up whatever the page's CSS gives that class, which with utility CSS includes its position and size, and so do its CSS custom properties; the box's containment is what holds such an element in place, so the page guards it there.

SVGs stored before 1.0.0-preview.2, and those stored by builds before this sanitiser, keep `<style>` elements, ids without a prefix and, before 1.0.0-preview.2, external links. They are safe where the CMS serves them, with its `Content-Security-Policy`, but not where a page inlines them or a CDN drops that header. `fluentcms media sanitize-svg`, run in the host project, sanitises them again with the current pass:

```bash
dotnet fluentcms media sanitize-svg --dry-run
dotnet fluentcms media sanitize-svg
```

It reads every SVG asset that is not deleted, in the library of each site (with `Library` set to `Shared`, the default site's only), under that site's media settings. With `--dry-run` it lists by site, id and file name each SVG whose bytes the pass would change, and changes nothing. Without it, each such SVG is replaced as `POST /media/{id}/replace` replaces it: the sanitised bytes under a key of its own, a new size, checksum and version, and `MediaAssetReplacedEvent`, so that the Delivery copies are evicted and webhooks hear of it. The old file goes as after a replace: it is deleted when no other asset names it, and otherwise kept by a holder until none does. The upload checks are not run again, so a site whose `svg` is now `refuse` still has its stored SVGs sanitised. An SVG whose bytes the pass keeps is left alone, so a second run changes nothing, and SVGs have no variants, so nothing else is made again. When it has replaced any, the command says that their checksums changed, and so did the `v` of their URLs: a URL with an old `v` still serves the new bytes, since `v` is signed and not compared, but a CDN serves the old bytes under it until its copy expires (`FluentCms:Media:CacheControl`). The event is written after the asset's row is saved and its old file deleted, so a run stopped between the two leaves that asset replaced without `MediaAssetReplacedEvent`, and a second run, which finds its bytes as the pass writes them, does not send it; its cached Delivery copies are then evicted by replacing the asset once more, or go when the cache expires.

The command leaves an SVG as it is, and names it on standard error, when it is larger than the site's `maxSvgFileSize`, which it does not parse (raise the setting for the run, or replace or delete the asset by hand), when the pass no longer takes its file as an SVG (replace or delete it by hand), when its file is missing, or when its replace is refused, such as by a write to the asset during the run. It counts each kind at the end, in a dry run too, and then ends with `FCMS3309` and exit code 1. A host without the media plugin ends with `FCMS3309` too. A package exported before the run whose line names an old key without the file is refused with `FCMS3220` on import, as such a line already is for those SVGs; a package that carries the file is sanitised and taken.

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
| `v` | A part of the asset's checksum, which the signature covers and nothing compares, so that a replace gives new URLs. |
| `sig` | The signature. |

A request over the limits of the site's media settings is refused with `FCMS1591` before the image is read. An image over `maxSourcePixels`, or whose side that follows the aspect ratio would be over the width or height limit, is refused from its header before it is decoded. Every result is stored as a variant in the asset's storage, so the same request, under any file name, is served from storage. Concurrent requests for a variant not yet stored wait for one decode, and at most `MaxConcurrentDecodes` images are decoded at once on an instance; an original in S3 or Azure Blob is read once, into a temporary file deleted after the decode, and an image far larger than the result is decoded at a smaller scale where its format allows, as JPEG and WebP do. The orientation of a photo is applied, and its EXIF, IPTC and XMP metadata are left out. A daily job, `media.variants.cleanup`, removes the variants of deleted assets.

Host code and other plugins build a URL with `IMediaService.GetImageUrlAsync`:

```csharp
var url = await media.GetImageUrlAsync(asset, new MediaImageOptions { Width = 1200, Height = 630, Fit = ImageFit.Cover, Format = MediaImageFormat.Auto });
// /media/0193a0c2-.../hero.jpg?w=1200&h=630&fit=cover&f=auto&v=...&sig=...
```

## Delivery API

`GET /api/delivery/v1/media/{id}` answers an asset's metadata, with `alt` and `caption` in the request's locale, and its URLs, relative to the base of the CMS:

```json
{
  "id": "0193a0c2-...", "fileName": "hero.jpg", "mimeType": "image/jpeg", "size": 48213, "width": 1600, "height": 900,
  "title": "Harbour", "locale": "en", "alt": "Boats in a harbour", "caption": null, "credit": null, "tags": [],
  "focalPoint": { "x": 0.5, "y": 0.4 }, "isPrivate": false, "createdAt": "...", "updatedAt": "...",
  "urls": {
    "original": "/media/0193a0c2-.../original/hero.jpg?v=...&sig=...",
    "presets": { "hero": "/media/0193a0c2-.../hero.jpg?preset=hero&v=...&sig=..." },
    "template": null
  }
}
```

The URLs are signed and carry `v` from the asset's checksum, so a replace gives the asset new URLs and no cache serves the old file under them. `presets` has one transform URL for each preset of the site, for an image the processor reads, and `template`, only where `RequireSignedUrls` is `false`, is a transform URL whose `{w}`, `{h}`, `{fit}`, `{q}` and `{f}` a client fills in or leaves out. The answer has the cache headers of content: an `ETag`, the `Cache-Control` of `FluentCms:Delivery:Cache`, and a copy in the output cache that a change to the asset or to the site's presets evicts. A private asset is answered, without URLs, only to a caller whose `media.read` reaches its folder; the anonymous caller and Delivery keys hold no permission but reading content, so they never read one.

## URLs for the media library and the picker

`GET /api/management/v1/media` and `GET /api/management/v1/media/{id}` give each asset its `urls`, signed and relative to the base of the CMS, so a grid of a page of assets needs no call per asset and no preset:

```json
{
  "id": "0193a0c2-...", "fileName": "hero.jpg", "mimeType": "image/jpeg", "isPrivate": false,
  "urls": {
    "original": "/media/0193a0c2-.../original/hero.jpg?v=3f2a9c0d41b7e8a6&sig=...",
    "thumbnail": "/media/0193a0c2-.../thumbnail/hero.jpg?v=3f2a9c0d41b7e8a6&sig=...",
    "expiresAt": null
  }
}
```

`thumbnail` is the image fitted inside 320 by 320 pixels, as WebP, for an image the processor reads, and null for any other file. Its URL takes no size and is always signed, whatever `RequireSignedUrls` says, so it serves that thumbnail of that asset only; it is not held to the site's transform limits, but still to `maxSourcePixels`. A public asset's URLs do not expire. A private asset's work until `expiresAt`, which is more than half of `downloadUrlSeconds` away and never more than all of it, so reads within that half give the same URLs. They serve the file inline to whoever holds them, with `Cache-Control: private` for the seconds left: the original as `/media/{id}/original/{fileName}?disposition=inline&exp=...&v=...&sig=...` and the thumbnail with `exp`. Read the asset again for new ones. Only a caller whose `media.read` reaches the asset's folder reads it, so no other caller gets its URLs. `v` is part of the checksum, so a replace gives new URLs. With `Library` set to `PerSite`, the URLs end with `site`, the site of the read, since an `<img>` sends no `X-Cms-Site`. The MCP tools answer the same assets without `urls`.

## Usage, delete and replace

`GET /api/management/v1/media/{id}/usage` lists the entries whose fields, blocks or rich text refer to the asset, as `{ "items": [{ "type", "entryId", "status", "locale", "field" }] }`, without the entries, locales and fields the caller cannot read. Entries in the trash are left out.

`DELETE /api/management/v1/media/{id}` refuses an asset that entries refer to (`FCMS1594`, 409). `?force=true` deletes it anyway and removes the references, so the fields that held it read as empty. A deleted asset keeps its file until `DeletedRetention` has passed, when the daily cleanup purges the row and, when the instance wrote the file, the file and the variant files. An asset whose key a line of an import named, which a storage shared with another instance can hold, goes with its rows only, and its file stays. A file or variant file that another asset of the library names stays, with the deleted asset's row, until no other asset names it, so the last asset to go deletes it. The variant files that an asset on another's key made go to the asset that wrote that key, and with it. The deletion of a site removes its assets in the same way. An import line without a file that names the key of a deleted asset, or a file that its asset no longer has, is refused with `FCMS3220`, since the cleanup deletes such a file.

A storage that two instances share is safe only when they share the database too. The checks above read this instance's database, so an instance that purges, replaces or removes a site deletes the files it wrote even when an asset that another instance's import named, by a line without a file, still serves them. Import such a package with `mediaFiles=true` instead, so that the asset takes a copy of its own.

`POST /api/management/v1/media/{id}/replace` with a `multipart/form-data` body of one file replaces the file of an asset (`media.update`). The file passes the checks of an upload. The id, the metadata and the references stay, the variants of the old file are removed, and `media.replaced` is raised. The old file and the variant files are deleted when the instance wrote the old file, unless another asset of the library names one of their keys: then they go to a deleted row that holds them, and the daily cleanup deletes each once no other asset names it. For a key that a line of an import named, which a storage shared with another instance can hold, they stay and only the variant rows go.

## Versions and If-Match

`GET /api/management/v1/media/{id}` answers the asset's version as its `ETag`, `"7"`. `PATCH`, `POST .../move` and `POST .../replace` answer the new one, and those three and `DELETE` take it back as `If-Match`: when the asset changed since, the write is refused with `412` and `FCMS3105`, as a write of an entry is, and nothing changes. Without `If-Match`, or with `*`, a write applies to whatever version is stored. The version is one for every locale of the asset.

## Original files and private downloads

`GET /media/{id}/original/{fileName}` sends a file as it was stored, or the `Range` of it asked for, with `X-Content-Type-Options: nosniff`, and an SVG with a `Content-Security-Policy` that runs nothing. A file whose stored type is none the plugin reads from bytes is sent as `application/octet-stream`, as an attachment. Without `exp`, it serves a public asset with a signature that the Delivery API's `urls.original` carries. A private asset is served only by a download URL:

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

Download URLs and the URLs of original files and thumbnails are signed with the same key. Replacing the key invalidates every URL signed before, in content and in CDN caches. To replace it, set `SigningKey`, or delete the row with id 1 of `cms_media_signing_key`, and restart every instance. Keep the Data Protection key ring in backups: without the key that protected the signing key, the instance cannot read it, and only setting `SigningKey` or deleting the row with id 1 brings transforms back.

## Media fields

The plugin adds the `media` field type. A content class marks a `MediaRef?` or a `MediaRefList` with `[Media]`, and a schema file uses `"type": "media"`, with `"multiple": true` for a list:

```csharp
[Media]
public MediaRef? Hero { get; set; }

[Media]
public MediaRefList Gallery { get; set; } = [];
```

A write sends asset ids and refuses one that is no asset of the site, or is deleted, with `FCMS2113`. A filter finds entries by asset id with `eq` and `in`, such as `filter[hero][eq]={id}`. A read gives `{ "id", "type": "media" }`, and `populate=hero` gives the asset: its file name, type, size, width and height, title, alternative text and caption in the locale of the read, credit, focal point, and `urls` as the Delivery API gives them (`original`, `presets` and, where unsigned URLs are served, `template`). `populate=sections` does the same for the media of a block list or a repeated component, and `populate=*` for all of them. On the Delivery API a public asset is populated for whoever reads, the Public role, a Delivery key and a preview token included, with the URLs `GET /api/delivery/v1/media/{id}` gives it, and a private asset, a deleted one and one of another site's library are null, as is every asset in a site where the plugin is disabled. So it is on any other surface, GraphQL for one, for a caller without the `management` scope. On the Management API, and elsewhere for a caller with the scope, the caller needs `media.read`, within its folders, to see an asset populated; one it cannot read is null, a private asset has `urls` null, and no asset is populated in a site where the plugin is disabled. A Delivery read that shows an asset is evicted from the output cache with the asset's own Delivery copy, when the asset changes, turns private or is deleted. With `richText=html` or `markdown`, a media node of rich text is written with the URL of the asset's original file and, when the node has no `alt`, the asset's alternative text, for the assets the same rule resolves; any other is written without a URL. Other plugins read assets the same way through `IMediaResolver`.

A field limits the types of asset it takes with `accept`: MIME types such as `image/png` and families such as `image/*`. In a class it is `[Media(Accept = ["image/*"])]`, and in a schema file `"accept": ["image/*"]`; no `accept`, or an empty list, takes every type. A type is letters and digits with `.`, `+`, `-` and `_`, a `/`, and a subtype of the same or `*`; anything else, `*/*` and parameters included, is refused at start with `FCMS1596`, which the analyzers report as `FCMS0017` while the code is written. An asset matches without regard to case and to parameters after `;`, so `image/*` takes `image/PNG`. A save or a publish whose field, at any depth, holds an asset of another type is refused (`FCMS1597` on the path of the field, in a 422 that names the field and its types). The rule is checked on every save, as a tightened length is: an entry saved before `accept` was added, or narrowed, keeps its asset until the next save of the entry, which is refused until the asset is replaced. An import's dry run does not check the types of the assets its package brings, which are not in the library until the import writes them, so the import may refuse an entry its dry run took. The admin's picker and its upload offer only those types, and the generated clients name them in the comment of the field.

Without the plugin, a type with a media field stops the host with `FCMS1203`.

## SkiaSharp

The default `IImageProcessor` uses [SkiaSharp](https://github.com/mono/SkiaSharp), under the MIT License, with `SkiaSharp.NativeAssets.Linux.NoDependencies`, so that it runs on Linux and in containers without fontconfig. It reads JPEG, PNG, GIF (its first frame) and WebP, and writes JPEG, PNG and WebP; it writes no AVIF and no GIF, so `f=avif` is refused and a GIF without `f` is served as a PNG. ImageSharp was not chosen: version 4 needs a Six Labors license key to build in Release, and the last version without that check has open advisories. A host that wants another processor, ImageSharp under its own Six Labors license included, replaces the provider slot:

```csharp
builder.Services.AddFluentCms(cms => cms.Providers.Use<IImageProcessor, MyImageProcessor>());
```
