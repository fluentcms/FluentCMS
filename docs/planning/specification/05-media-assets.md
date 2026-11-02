# 5. Media & assets — approved

Media is a first-party plugin on the provider slots: a storage abstraction with local, S3-compatible and Azure Blob providers, and on-demand image transforms served from a signed URL. Images are the most common reason frontends call a CMS, so transforms, focal point and format negotiation are in the first release.

| # | Feature | Tier | What it means |
| --- | --- | --- | --- |
| 1 | Storage provider abstraction | Must | `IFileStorage` with providers for local disk, S3-compatible (AWS, R2, MinIO) and Azure Blob; per-site provider selection; streaming upload and download. |
| 2 | Upload | Must | Multi-file upload via Management API and admin, drag-and-drop and paste, size limits, MIME allow-list with content sniffing, executable and SVG script blocking. |
| 3 | Asset metadata | Must | Title, alt text, caption, credit, tags, dimensions, duration, MIME type, size, checksum; localizable alt and caption. |
| 4 | Folders | Must | Nested folders with move, rename and per-folder permissions. |
| 5 | On-demand image transforms | Must | URL parameters for width, height, fit (cover, contain, fill), quality, format (auto WebP/AVIF by `Accept`), focal-point crop; results cached in storage; limits on dimensions and operations per request. |
| 6 | Signed transform URLs | Must | HMAC-signed parameters so only server-issued variants can be generated; unsigned mode optional for trusted setups. |
| 7 | Focal point and crop presets | Must | Focal point set in the admin; named crop presets per site (hero, thumbnail) usable in URLs and SDKs. |
| 8 | Image processing provider | Must | ImageSharp by default behind an `IImageProcessor` slot; replaceable by a CDN transformer (Cloudflare Images, Imgix) that maps the same URL grammar. |
| 9 | Media Delivery API | Must | Public read of asset metadata and URLs with the same key classes and caching rules as content; `srcset` helpers in the SDKs. |
| 10 | Usage tracking | Must | Which entries reference an asset; block delete or warn when in use; replace file keeping the ID. |
| 11 | Private assets | Must | Assets gated by access rules, served via short-lived signed download URLs; never public by storage default. |
| 12 | Media in rich text and blocks | Must | Media references in rich text and block fields resolve through the same picker, metadata and transforms. |
| 13 | Bulk operations | Should | Bulk move, tag, delete, replace alt text; bulk download as zip. |
| 14 | Video and audio handling | Should | Poster frame extraction, duration and dimensions, HTML5 playback in admin; no transcoding. |
| 15 | Chunked and resumable upload | Should | TUS-style resumable upload for large files; client-direct upload to storage with pre-signed URLs. |
| 16 | Document previews | Should | PDF first-page thumbnails and office document icons in the library. |
| 17 | Upload from URL | Should | Import by URL with an allow-list and size cap. |
| 18 | Duplicate detection | Should | Checksum-based warning on upload of an existing file. |
| 19 | Trash for media | Could | Soft delete with restore and retention. |
| 20 | External asset sources | Could | Browse and link Cloudinary, Bynder or DAM assets without copying. |
| 21 | Automatic image metadata | Could | EXIF extraction, dominant color, blur-hash placeholders. |
| 22 | AI alt text and tagging | Could | Via the AI assist plugin, suggested on upload, never auto-applied without review. |
| 23 | Media versioning | Could | Keep previous files on replace, with restore. |
| 24 | Video transcoding | Won't | Out of scope; integrate a streaming provider instead. |
| 25 | Built-in CDN | Won't | Cache headers and purge hooks only; CDN is the host's choice. |
| 26 | Image editing beyond crop and focal point | Won't | No filters, drawing or retouching. |

## Decisions this category forces

- [ ] Transform URL grammar: path segments (`/media/{id}/w_800,h_600,c_fill/name.jpg`) or query string (`?w=800&h=600&fit=cover`). Recommendation: query string for readability and SDK generation, with signature as a parameter.
- [ ] Variant cache location: alongside the original in the same storage provider (recommended) or a separate cache store.
- [ ] Asset identity in URLs: GUID only, or GUID plus original filename for SEO. Recommendation: GUID plus filename, filename ignored on lookup.
- [ ] Local provider in production: allowed with a documented warning, or dev-only. Recommendation: allowed; small sites and the reference host need it.
