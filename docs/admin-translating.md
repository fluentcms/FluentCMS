# Translating the admin

The admin ships in English, with every text in resource files ready for translations. A translation is a set of resource files and nothing else: the build makes each file a satellite assembly, the admin offers every culture it has files for, and the browser fetches the satellite assemblies of the culture the admin starts in, and of a lazy assembly when that loads. This page is for a translator and for a reviewer of a translation. The [admin overview](admin.md#language-and-formats) says how a user picks a language, and [admin configuration](admin-configuration.md#cultures) how an instance limits the languages it offers.

## The resource files

Each assembly of the admin carries its texts in `.resx` files beside its code, one neutral file in English per group of texts:

| Assembly | Neutral files |
| --- | --- |
| `FluentCMS.Admin` | `admin/src/FluentCMS.Admin/Localization/AdminResources.resx`: the shell, the sign-in, the lists and the forms |
| `FluentCMS.Admin.Components` | `admin/src/FluentCMS.Admin.Components/Resources/CmsStrings.resx`: the texts of the wrapped set, and those of the component library under it, under the library's own keys |
| `FluentCMS.Admin.Administration` | one file per area, such as `Users/UsersResources.resx` and `Account/AccountResources.resx`, and `Common/CommonResources.resx` |
| `FluentCMS.Admin.RichText` | `admin/src/FluentCMS.Admin.RichText/RichTextResources.resx` |
| the first-party admin halves | one file each under `admin/src/plugins/`, such as `FluentCMS.Plugins.Media.Admin/MediaResources.resx` |

A translation adds, beside each neutral file, a file named for the culture: `AdminResources.fr.resx` beside `AdminResources.resx`, `UsersResources.fr.resx` beside `UsersResources.resx`, and so on for every neutral file. The culture is a culture name such as `fr`, `pt-BR` or `zh-Hans`. A neutral language, such as `fr`, serves every region of it, since a browser in `fr-CA` gets the closest culture the admin has. Keep each key and translate each `<value>`; the `<comment>` of an entry, where there is one, says where the text shows and what its placeholders hold.

Two files are not part of a translation:

- `admin/src/FluentCMS.Admin.Administration/Gallery/GalleryResources.resx`, the texts of the component gallery, a page for developers.
- The hosted sign-in pages of the API host, which are the host's and not the admin's.

The culture's files make it a culture the admin ships: the build of `FluentCMS.Admin` names English and the cultures of its own resource files, so a culture appears once `AdminResources.{culture}.resx` exists. Then:

- **The account page** names each language in its own language, from `Account.Preferences.Language.{culture}` of `AccountResources.resx` when that key exists, and else from the platform's culture data. To set the name, add the key with the language's own name, such as `Français` for `fr`, to the neutral file and to every culture's file, since the test below asks each culture for every key.
- **The menu items of the halves.** The sidebar shows a half's menu item before the half is loaded, from the half's manifest, so a translation also adds the culture's text to `labels` of each menu item in `wwwroot/fluentcms-admin.json` of each first-party half, such as `"labels": { "en": "Media", "fr": "Médias" }`. Each half's manifest test fails without it once the half has a resource file of the culture ([writing an admin half](admin-plugins.md#resources-and-menu-texts)).

A team's admin app names its content types in a resource file of its own, read before the admin's for `ContentType.{name}`, such as `ContentType.article`, with `AddFluentCmsAdmin<AppResources>()` in its `Program.cs` ([`FluentCMS.Admin`](../admin/src/FluentCMS.Admin/README.md)). Its translations are files of the same kind, in the team's app.

## Placeholders

A text with a value in it holds format items, such as `{0} of {1} entries`. A translation may move an item and put words around it, and may not drop one or add one. A doubled brace, `{{` or `}}`, is a brace of the text and not an item. A format inside an item, such as `{0:t}`, is the formatting of the value and may be left as it is.

## Right to left

The direction of the interface follows the culture the admin runs in: right to left for a culture whose script is Arabic, Hebrew, Thaana, Syriac, N'Ko, Adlam or Hanifi Rohingya, such as `ar`, `he`, `fa` or `ur`, or whose script subtag names one, such as `az-Arab`; left to right otherwise, as for `ar-Latn`. The admin decides this from the culture's name and not from the culture data of the browser's runtime, which has most right-to-left locales wrong. The layout uses logical properties only, so it mirrors without a change to a file. A field of an entry keeps the direction of its content locale, so an Arabic interface shows an English entry left to right.

A translation into a right-to-left language checks the admin under the pseudo-culture `ar-XB` first, and then under its own culture.

## The test of the keys

`ResourceKeysTests` of `admin/tests/FluentCMS.Admin.Tests` compares each culture's file of each assembly under `admin/src` with its neutral file, and names a key the culture lacks, a key the neutral file lacks, and a text whose format items differ from the neutral text's:

```bash
dotnet test --project admin/tests/FluentCMS.Admin.Tests -c Release --filter-class FluentCMS.Admin.Tests.ResourceKeysTests
```

With English alone it compares no culture's file, so it is also run on the pseudo-cultures, which are written from every neutral file. The test runs in the fast checks of every pull request.

## The pseudo-cultures

Two pseudo-cultures check that every text the admin shows comes from a resource, without a translation:

- `en-XA` accents the letters of each text and brackets it, `⟦Šàvé⟧`, so a text that shows unaccented or unbracketed is hard-coded, and a text cut short shows without its closing bracket.
- `ar-XB` brackets each text and writes it right to left, and runs the interface right to left, so a layout that does not mirror shows.

They are no culture of a release. A build or a publish with `-p:FluentCmsAdminPseudoCultures=true` writes an `en-XA` and an `ar-XB` file of every neutral resource file into `obj`, as `admin/eng/PseudoCulture.cs` marks them, and adds them as satellite assemblies. A pack with the property stops, and the size check of the `Pack` job fails a publish that has them, so no package or release carries them.

To run the admin in one, build the reference host with the property, and offer the pseudo-culture alone, so that the admin starts in it whatever the browser's language:

```bash
dotnet build src/FluentCMS.Host -p:FluentCmsAdminPseudoCultures=true
FluentCms__Admin__Cultures__Offered__0=ar-XB FluentCms__Admin__Cultures__Default=ar-XB \
  dotnet run --project src/FluentCMS.Host --no-build
```

Then open `https://localhost:5001/admin/`, as for the local loop of [host configuration](host-configuration.md#admin). A user cannot save a pseudo-culture as a preference, since the server's platform does not know it.

The component tests sweep every routable page of each first-party assembly under both pseudo-cultures (`PseudoSweep` and `UsePseudoCulture` of `admin/tests/Shared`), and a page added without a sweep fails a test. The end-to-end tests run the admin in `ar-XB` on a publish with the property, and check every view with axe-core right to left too.

## Sending a translation

1. **Open an issue** that names the culture, as every change starts from one ([CONTRIBUTING.md](../CONTRIBUTING.md#workflow-issue-first)), and wait for a maintainer to confirm it.
2. **Branch from `main`**, named `<issue-number>-<short-slug>`.
3. **Add the culture's file** beside each neutral file under `admin/src`, the gallery's aside, with every key.
4. **Add the culture's text** to `labels` of each menu item in the manifests of the first-party halves under `admin/src/plugins/`.
5. **Run the test of the keys** and the tests of the halves, whose manifest tests check the menu texts:

   ```bash
   dotnet test --project admin/tests/FluentCMS.Admin.Tests -c Release
   dotnet test --project admin/tests/FluentCMS.Plugins.Media.Admin.Tests -c Release
   ```

   and the same for the webhooks, MCP, AI assist, multi-site and search halves (`admin/tests/FluentCMS.Plugins.*.Admin.Tests`). The search half has no menu item, so its manifest needs no text of the culture.
6. **Run the admin in the culture**, with `FluentCms__Admin__Cultures__Offered__0` and `FluentCms__Admin__Cultures__Default` set to it as above, from a build without the property, and look at the pages a user of the culture uses most.
7. **Open the pull request** with `Closes #<issue-number>`, as [CONTRIBUTING.md](../CONTRIBUTING.md#pull-requests) says.

A translation can be partial only in the sense that a key's value may stay in English: the test asks for every key, not for a translated text.
