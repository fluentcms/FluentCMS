# FluentCMS.Plugins.Media

Assets and folders for the sites of an instance: uploads whose type is read from their bytes, metadata with alternative text and captions per locale, and file storage on the local disk. The media is managed under `/api/management/v1/media`. Section 5.1 of the [backend technical specification](../../../docs/planning/backend-technical-spec.md) has the full design.

## Options

The instance's options are in `FluentCms:Media`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Local:Root` | `App_Data/media` | The directory of the local storage, under the content root unless it is absolute. |
| `MaxRequestBodySize` | `268435456` | The largest upload request, in bytes. A site's `MaxRequestSize` above it is cut to it. |

## Site settings

The rules of uploads are each site's, in the settings area `media` (`settings.media.manage`):

| Setting | Default | Meaning |
| --- | --- | --- |
| `maxFileSize` | 50 MB | The largest file, in bytes. |
| `maxRequestSize` | 200 MB | The largest total of the files of one request, in bytes. |
| `maxFilesPerRequest` | 20 | The most files in one request. |
| `allowedTypes` | every type below | The types a site accepts: `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `image/avif`, `application/pdf`, `video/mp4`, `video/webm`, `audio/mpeg` and `audio/wav`. |
| `svg` | `refuse` | `refuse` refuses every SVG; `sanitize` stores it without its scripts, event handlers, `foreignObject` and `javascript:` URLs. |

A type comes from the first bytes of the file, never from its name or `Content-Type`. Executables are always refused, by their signature (Windows, ELF, Mach-O, a shebang script) and by an executable or script extension.

## Local storage in Production

The local disk is allowed in Production, and the startup report lists `media-local-storage` there. The files live on the machine of the instance, so:

- every instance of a deployment needs `Local:Root` on one shared volume;
- backups must include that directory, beside the database;
- a container needs the directory on a volume, or the files go with the container.

A storage provider such as S3 or Azure Blob replaces the provider slot `IFileStorage`. Each asset records the storage it was written with, so assets written before a change stay where they are.

## Permissions

`media.read`, `media.upload`, `media.update`, `media.delete` and `media.folders.manage`. A grant can be limited to folders with the `resources` constraint, listing folder ids; it then reaches those folders and their subfolders only, and not the root:

```json
{ "permission": "media.*", "constraints": { "resources": ["0193a0c2-5f4e-7c1d-9a3b-2b8f6e1d4c70"] } }
```

## Upload

`POST /api/management/v1/media` with a `multipart/form-data` body. Every file part is a file of the upload, and the field `folderId` names the folder, or leave it out for the root. A request stores all its files or none, and answers 201 with the new assets:

```bash
curl -H "Authorization: ApiKey $KEY" -F folderId=$FOLDER -F files=@photo.jpg -F files=@report.pdf https://cms.example.com/api/management/v1/media
```
