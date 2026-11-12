# Analyzer rules

`FluentCMS.Analyzers` reports in the editor and the build what schema validation would refuse when the host starts. Each rule below is the error reference of its code, and the help link of the diagnostic points here. A rule reports a schema declared with the attributes; a schema from the fluent builder or a schema file is checked by `fluentcms schema validate` and at start.

The codes `FCMS0001` to `FCMS0099` belong to the analyzers, and `FCMS0100` to `FCMS0119` to the source generator. Every rule but `FCMS0060` stands for a code of schema validation (`FCMS12xx`), which its message names: fixing what the rule reports removes that error at start.

## Referencing the analyzers

A host or a plugin references the package as an analyzer only, so it is not a dependency of what they ship:

```xml
<ItemGroup>
  <PackageReference Include="FluentCMS.Analyzers" Version="..." PrivateAssets="all" />
</ItemGroup>
```

Inside this repository a project references `src/FluentCMS.Analyzers/FluentCMS.Analyzers.csproj` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`, and a packable project adds `PrivateAssets="all"`. The reference host, the Blog sample and the first-party plugins do. The analyzers need the compiler of the .NET 10 SDK, 10.0.100 or later.

## Attribute combinations

### FCMS0001

Error, for `FCMS1215`. A class has more than one of `[Collection]`, `[Singleton]`, `[Component]` and `[Block]`. Keep one.

### FCMS0002

Error, for `FCMS1215`. A collection or singleton does not derive from `ContentItem`, or a component or block does. Change the base class.

### FCMS0003

Error, for `FCMS1215`. A component or block has `[PublishPolicy]`. Only collections and singletons are published; remove the attribute.

### FCMS0004

Error, for `FCMS1216`. A property has schema attributes such as `[Required]` but no field type attribute such as `[Text]`. Add the field type, or remove the other attributes if the property is not a field.

### FCMS0005

Error, for `FCMS1216`. A property has more than one field type attribute. Keep one.

### FCMS0006

Error, for `FCMS1216`. A field property has no public getter or no public setter. An `init` setter is public.

### FCMS0007

Error, for `FCMS1216`. `[Select]` on an enum lists options. The members of the enum are the options; remove the list.

### FCMS0008

Error, for `FCMS1216`. `[Select]` on a property that is not an enum lists no options. List them, as in `[Select("news", "blog")]`.

### FCMS0009

Error, for `FCMS1216`. A `[Relation]` property is not a `Ref<T>` or a `RefList<T>`.

### FCMS0010

Error, for `FCMS1210`. A `[Component]` property holds a class, or a list of a class, that is not marked with `[Component]`.

### FCMS0011

Error, for `FCMS1211`. `[Blocks]` lists no block types. List them, as in `[Blocks(typeof(HeroBlock))]`.

### FCMS0012

Error, for `FCMS1210`. `[Blocks]` lists a class that is not marked with `[Block]`.

### FCMS0013

Error, for `FCMS1216`. `[Text(MaxLength = n)]` and `[Length(Max = m)]` set two different maximum lengths. Keep one, or make them equal.

### FCMS0014

Error, for `FCMS1211`. A list or an array holds a field type that holds a single value, such as `[Text]` on a `List<string>`. Only select, relation, component and media fields may be lists.

### FCMS0015

Error, for `FCMS1211`. `[Unique]` is on a field that has no column to index: a relation field, which is stored as a reference, or a rich text, JSON, blocks, multiple select or repeated component field, which is stored as JSON. Inside a single component the rule reports the field of the component, for each content type that holds it.

### FCMS0016

Error, for `FCMS1217`. A relation of a component or block declares `Reverse`. Only a field of a collection or singleton may; declare it there.

## Names

### FCMS0020

Error, for `FCMS1206`. A field takes a reserved name: a system field of `ContentItem` or `fallbacks` (without regard to case), `type` or `id` in a block, a name whose column is a system column (`documentId`, `siteId`, `state`), or a reverse relation named like a system field.

### FCMS0021

Error, for `FCMS1206`. A collection or singleton takes a name whose table is a system table, such as `user` (`cms_user`). Components and blocks have no table and may use these names.

### FCMS0022

Error, for `FCMS1218`. A type name has more than 64 characters, or the table of a collection or singleton (`cms_{type}`, `cms_{type}_i18n` when it is localized) has more than 63.

### FCMS0023

Error, for `FCMS1218`. A field name has more than 64 characters, its column has more than 63 (a field of a single component is a column named after the field that holds it, as `seo_meta_title`), or the path of a relation field (`seo.author`) has more than 64. A media field is left to the host, since the analyzers cannot tell whether the media plugin registers its type.

## Slugs

### FCMS0030

Error, for `FCMS1205`. The `Source` of `[Slug]` is not another field of the type. Name a field, as in `[Slug(Source = nameof(Title))]`.

## Relations

### FCMS0040

Error, for `FCMS1204`. The target of a relation is not marked with `[Collection]` or `[Singleton]`.

## Localization

### FCMS0050

Warning, for `FCMS1208`. A field is `[Localizable]` in a collection or singleton that is not localized, or in a component or block, whose fields follow the field that holds them. Set `Localized = true` on the type, or remove the attribute. The host still refuses the schema at start.

## Queries

### FCMS0060

Information. A filter or sort of the generated builders, such as `ArticleFields.Title.Contains("x")`, is on a field that has no index, so the query reads every row of the type. Mark the field `[Indexed]` if the query is frequent; a `[Unique]` field has an index already. The rule reports the fields that have a column of their own: text, slug, email, URL, integer, decimal, boolean, date, date and time, and single select fields, of a collection, a singleton or a single component. Schema validation accepts such a field, so the rule stands for no code.
