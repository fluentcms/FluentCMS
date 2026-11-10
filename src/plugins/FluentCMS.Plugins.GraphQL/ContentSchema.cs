using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Publishing;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;

namespace FluentCMS.Plugins.GraphQL;

// Builds the HotChocolate types of the content schema: an object type per content type, component and block, a union per
// block list, an enum per select, the filter, sort and write inputs of each content type, and its root fields. A type
// refers to another by name, since none of them has a class.
internal sealed class ContentSchema(ContentModel model, int defaultPageSize)
{
    private const string LocaleNote = "The locale to read, one of the site's. Left out, it is the locale of the request.";

    private readonly Dictionary<string, string> _operatorInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _filters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _componentFilters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _componentInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _relationFilters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rootFields = new(StringComparer.Ordinal);
    private readonly HashSet<string> _names = new(model.Names, StringComparer.Ordinal);
    private readonly List<ITypeDefinition> _types = [];

    public void AddTo(ISchemaBuilder schema)
    {
        schema
            .AddType(new AnyType(ScalarNames.Json, "A JSON value.", BindingBehavior.Explicit))
            .AddType<LongType>()
            .AddType<DecimalType>()
            .AddType<DateTimeType>()
            .AddType<DateType>()
            .AddType(new ObjectType<PublishWarning>(warning => warning.Name(ContentModel.PublishWarningType)))
            .AddType(new ObjectType(reference =>
            {
                reference.Name(ContentModel.EntryReference).Description("A relation that is not populated: the id and the content type of its target.");
                reference.Field("id").Type<NonNullType<IdType>>().Cost(0).Resolve(context => EntryValues.Id(context.Parent<JsonObject>()));
                reference.Field("type").Type<StringType>().Cost(0).Resolve(context => EntryValues.BlockType(context.Parent<JsonObject>()));
            }));

        foreach (var type in model.Components.Concat(model.Blocks).Concat(model.ContentTypes))
        {
            AddEnums(type);
            _types.Add(Object(type));
        }

        List<Action<IObjectTypeDescriptor>> queries = [];
        List<Action<IObjectTypeDescriptor>> mutations = [];
        foreach (var type in model.ContentTypes)
        {
            AddRoots(type, queries, mutations);
        }

        foreach (var type in _types)
        {
            schema.AddType(type);
        }

        schema.AddQueryType(query =>
        {
            query.Name("Query");
            if (queries.Count == 0)
            {
                query.Field("_empty").Type<BooleanType>().Description("The content schema has no content types.").Resolve(_ => null);
            }

            queries.ForEach(add => add(query));
        });

        if (mutations.Count > 0)
        {
            schema.AddMutationType(mutation =>
            {
                mutation.Name("Mutation");
                mutations.ForEach(add => add(mutation));
            });
        }
    }

    private static NamedTypeNode Named(string name) => new(name);

    private static NonNullTypeNode Required(INullableTypeNode type) => new(type);

    private static ListTypeNode ListOf(string name) => new(Required(Named(name)));

    private static ObjectType Object(TypeModel type) => new(descriptor =>
    {
        descriptor.Name(type.Name);
        if (type.Role == TypeRole.Content)
        {
            foreach (var system in SystemFieldModel.All.Where(system => system.Selectable))
            {
                var field = descriptor.Field(system.Name).Cost(0).Resolve(context => ContentResolvers.SystemField(context, type, system));
                _ = system.Name switch
                {
                    "id" => field.Type<NonNullType<IdType>>(),
                    SystemFieldModel.Status => field.Type<EnumType<EntryStatus>>(),
                    _ => field.Type(Named(system.Scalar)),
                };
            }
        }

        foreach (var field in type.Fields)
        {
            // A field is read from the entry its root field loaded, so it costs nothing, but one that a read populates,
            // a relation or a media field, adds its targets to that read.
            descriptor.Field(field.Name)
                .Type(OutputType(field))
                .Cost(field is { Kind: FieldKind.Relation, ReferenceOnly: false } or { Kind: FieldKind.Reference } ? 1 : 0)
                .Resolve(context => ContentResolvers.Field(context, type, field));
        }
    });

    private static ITypeNode OutputType(FieldModel field)
    {
        var name = field.Kind switch
        {
            FieldKind.Enum or FieldKind.Blocks => field.TypeName!,
            FieldKind.Component => field.Target!.Name,
            FieldKind.Relation => field.ReferenceOnly ? ContentModel.EntryReference : field.Target!.Name,
            FieldKind.Reference or FieldKind.Json or FieldKind.RichText => ScalarNames.Json,
            _ => field.Scalar,
        };

        return field.Multiple && field.Kind is not (FieldKind.Reference or FieldKind.Json or FieldKind.RichText) ? ListOf(name) : Named(name);
    }

    private void AddEnums(TypeModel type)
    {
        foreach (var field in type.Fields)
        {
            switch (field.Kind)
            {
                case FieldKind.Enum:
                    _types.Add(new EnumType(descriptor =>
                    {
                        descriptor.Name(field.TypeName!);
                        foreach (var (name, option) in field.OptionsByName)
                        {
                            descriptor.Value(option).Name(name);
                        }
                    }));
                    break;
                case FieldKind.Blocks:
                    _types.Add(new UnionType(descriptor =>
                    {
                        descriptor.Name(field.TypeName!);
                        foreach (var block in field.BlockNames)
                        {
                            descriptor.Type(Named(model.FindBlock(block)!.Name));
                        }

                        descriptor.ResolveAbstractType((context, result) =>
                            result is JsonObject item && EntryValues.BlockType(item) is { } name && model.FindBlock(name) is { } found
                                ? context.Schema.Types.GetType<ObjectType>(found.Name)
                                : null);
                    }));
                    break;
            }
        }
    }

    private void AddRoots(TypeModel type, List<Action<IObjectTypeDescriptor>> queries, List<Action<IObjectTypeDescriptor>> mutations)
    {
        var single = RootField(GraphQLNames.Camel(type.SchemaName), type);
        if (type.Schema.Kind == FluentCMS.Schema.SchemaTypeKind.Singleton)
        {
            queries.Add(query => ReadArguments(query.Field(single).Type(Named(type.Name)).Resolve(ContentResolvers.Guard(context => ContentResolvers.SingletonAsync(context, type)))));
        }
        else
        {
            AddCollection(type, single, queries);
        }

        AddMutations(type, mutations);
    }

    private void AddCollection(TypeModel type, string single, List<Action<IObjectTypeDescriptor>> queries)
    {
        var list = Claim(type.Name + "List", type.SchemaName);
        _types.Add(new ObjectType(descriptor =>
        {
            descriptor.Name(list);
            descriptor.Field(ContentResolvers.Items).Type(Required(ListOf(type.Name))).Cost(0).Resolve(context => context.Parent<EntryList>().Items);
            descriptor.Field(ContentResolvers.Total).Type<LongType>().Description("The number of entries the filter matches. Selecting it runs a second query.").Resolve(context => context.Parent<EntryList>().Total);
            descriptor.Field("page").Type<IntType>().Cost(0).Resolve(context => context.Parent<EntryList>().Page);
            descriptor.Field("pageSize").Type<NonNullType<IntType>>().Cost(0).Resolve(context => context.Parent<EntryList>().PageSize);
            descriptor.Field("nextCursor").Type<StringType>().Description("The cursor of the next page, to send back as it came.").Cost(0).Resolve(context => context.Parent<EntryList>().NextCursor);
        }));

        var filter = Filter(type);
        var (sort, sortPaths) = Sort(type);
        var listField = RootField(single + "List", type);
        queries.Add(query =>
        {
            var field = query.Field(listField)
                .Type(Named(list))
                .Argument(Arguments.Filter, argument => argument.Type(Named(filter)))
                .Argument(Arguments.Sort, argument => argument.Type(ListOf(sort)))
                .Argument(Arguments.Search, argument => argument.Type<StringType>().Description("Full text search, which needs a search index."))
                .Argument(Arguments.Page, argument => argument.Type<IntType>().Description("The page, from 1."))
                .Argument(Arguments.Offset, argument => argument.Type<IntType>().Description("The entries to skip, in place of a page."))
                .Argument(Arguments.PageSize, argument => argument.Type<IntType>())
                .Argument(Arguments.Cursor, argument => argument.Type<StringType>().Description("The nextCursor of the page before, in place of a page or an offset."))
                .ListSize(assumedSize: defaultPageSize, slicingArguments: [Arguments.PageSize], sizedFields: [ContentResolvers.Items], requireOneSlicingArgument: false)
                .Resolve(ContentResolvers.Guard(context => ContentResolvers.ListAsync(context, type, list, sortPaths)));
            ReadArguments(field);
        });

        // A unique field is an argument of its own, unless it is named like one of the others.
        var unique = type.Fields
            .Where(field => field is { Kind: FieldKind.Scalar, Multiple: false, Field.Unique: true } && !Arguments.Read.Contains(field.Name))
            .Select(field => field.Name)
            .ToList();
        queries.Add(query =>
        {
            var field = query.Field(single)
                .Type(Named(type.Name))
                .Argument(Arguments.Id, argument => argument.Type<IdType>())
                .Resolve(ContentResolvers.Guard(context => ContentResolvers.SingleAsync(context, type, unique)));
            foreach (var name in unique)
            {
                field.Argument(name, argument => argument.Type<StringType>().Description($"The value of the unique field {name}, in place of the id."));
            }

            ReadArguments(field);
        });
    }

    private static void ReadArguments(IObjectFieldDescriptor field) => field
        .Argument(Arguments.Locale, argument => argument.Type<StringType>().Description(LocaleNote))
        .Argument(Arguments.Status, argument => argument.Type<EnumType<QueryStatus>>().Description("PUBLISHED unless DRAFT is asked, which needs a preview key, the draft scope or a managing principal."))
        .Argument(Arguments.Fallback, argument => argument.Type<EnumType<LocaleFallback>>())
        .Argument(Arguments.RichText, argument => argument.Type<EnumType<RichTextFormat>>().Description("How rich text is returned: its JSON document, or HTML or Markdown text."))
        .Serial();

    private void AddMutations(TypeModel type, List<Action<IObjectTypeDescriptor>> mutations)
    {
        var entry = Named(type.Name);
        if (Input(type) is { } input)
        {
            var create = RootField("create" + type.Name, type);
            var update = RootField("update" + type.Name, type);
            mutations.Add(mutation => mutation.Field(create)
                .Type(entry)
                .Argument(Arguments.Input, argument => argument.Type(Required(Named(input))))
                .Argument(Arguments.Locale, argument => argument.Type<StringType>().Description(LocaleNote))
                .Resolve(ContentResolvers.Guard(context => ContentResolvers.CreateAsync(context, type))));
            mutations.Add(mutation => mutation.Field(update)
                .Description("Changes the fields the input names and keeps the others; null clears a field.")
                .Type(entry)
                .Argument(Arguments.Id, argument => argument.Type<NonNullType<IdType>>())
                .Argument(Arguments.Input, argument => argument.Type(Required(Named(input))))
                .Argument(Arguments.ExpectedVersion, argument => argument.Type<NonNullType<IntType>>().Description("The version the change is based on; 0 adds a locale the entry does not have."))
                .Argument(Arguments.Locale, argument => argument.Type<StringType>().Description(LocaleNote))
                .Resolve(ContentResolvers.Guard(context => ContentResolvers.UpdateAsync(context, type))));
        }

        var payload = Claim(type.Name + "PublishResult", type.SchemaName);
        _types.Add(new ObjectType(descriptor =>
        {
            descriptor.Name(payload);
            descriptor.Field(ContentResolvers.Entry).Type(entry).Cost(0).Resolve(context => context.Parent<PublishPayload>().Entry);
            descriptor.Field("warnings").Type(Required(ListOf(ContentModel.PublishWarningType))).Cost(0).Resolve(context => context.Parent<PublishPayload>().Warnings);
        }));

        var delete = RootField("delete" + type.Name, type);
        var publish = RootField("publish" + type.Name, type);
        var unpublish = RootField("unpublish" + type.Name, type);
        mutations.Add(mutation => mutation.Field(delete)
            .Description("Moves the entry to the trash, in every locale, and answers its id.")
            .Type<IdType>()
            .Argument(Arguments.Id, argument => argument.Type<NonNullType<IdType>>())
            .Resolve(ContentResolvers.Guard(context => ContentResolvers.DeleteAsync(context, type))));
        mutations.Add(mutation => mutation.Field(publish)
            .Type(Named(payload))
            .Argument(Arguments.Id, argument => argument.Type<NonNullType<IdType>>())
            .Argument(Arguments.ExpectedVersion, argument => argument.Type<IntType>().Description("Publishes only if the draft is still at this version."))
            .Argument(Arguments.Locale, argument => argument.Type<StringType>().Description(LocaleNote))
            .Resolve(ContentResolvers.Guard(context => ContentResolvers.PublishAsync(context, type, payload))));
        mutations.Add(mutation => mutation.Field(unpublish)
            .Type(entry)
            .Argument(Arguments.Id, argument => argument.Type<NonNullType<IdType>>())
            .Argument(Arguments.Locale, argument => argument.Type<StringType>().Description(LocaleNote))
            .Resolve(ContentResolvers.Guard(context => ContentResolvers.UnpublishAsync(context, type))));
    }

    private string Claim(string name, string source) => ContentModel.Claim(_names, name, source);

    private string RootField(string name, TypeModel type) => ContentModel.Claim(_rootFields, name, type.SchemaName);

    // The filter of a content type: and, or and not, its system fields, and each field its field type can filter. The
    // inputs it refers to are made here and not while HotChocolate reads the type, which is after the types are handed over.
    private string Filter(TypeModel type)
    {
        if (_filters.TryGetValue(type.SchemaName, out var known))
        {
            return known;
        }

        var name = Claim(type.Name + "Filter", type.SchemaName);
        _filters[type.SchemaName] = name;
        List<(string Field, ITypeNode Type)> fields = [(FilterReader.And, ListOf(name)), (FilterReader.Or, ListOf(name)), (FilterReader.Not, Named(name))];
        foreach (var system in SystemFieldModel.All.Where(system => system.Operators.Count > 0))
        {
            var operand = system.Name == SystemFieldModel.Status ? nameof(EntryStatus) : system.Scalar;
            fields.Add((system.Name, Named(Operators("System" + GraphQLNames.Pascal(system.Name), operand, system.Operators))));
        }

        fields.AddRange(FilterFields(type));
        _types.Add(InputOf(name, fields));
        return name;
    }

    private static InputObjectType InputOf(string name, IReadOnlyList<(string Field, ITypeNode Type)> fields) => new(descriptor =>
    {
        descriptor.Name(name);
        foreach (var (field, type) in fields)
        {
            descriptor.Field(field).Type(type);
        }
    });

    private List<(string Field, ITypeNode Type)> FilterFields(TypeModel type)
    {
        List<(string Field, ITypeNode Type)> fields = [];
        foreach (var field in type.Fields.Where(field => field.Name is not (FilterReader.And or FilterReader.Or or FilterReader.Not)))
        {
            if (field is { Kind: FieldKind.Component, Multiple: false })
            {
                if (ComponentFilter(field.Target!) is { } component)
                {
                    fields.Add((field.Name, Named(component)));
                }
            }
            else if (field is { Kind: FieldKind.Relation, ReferenceOnly: false })
            {
                fields.Add((field.Name, Named(RelationFilter(field))));
            }
            else if (field.Operators.Count > 0)
            {
                var operand = field.Kind == FieldKind.Enum ? field.TypeName! : field.Scalar == ScalarNames.Json ? ScalarNames.String : field.Scalar;
                fields.Add((field.Name, Named(Operators(GraphQLNames.Pascal(field.Field.FieldType), operand, field.Operators))));
            }
        }

        return fields;
    }

    // The fields of a single component are columns of the entry, filtered by a dotted path.
    private string? ComponentFilter(TypeModel component)
    {
        if (_componentFilters.TryGetValue(component.SchemaName, out var known))
        {
            return known;
        }

        var filterable = component.Fields.Any(field => field.Operators.Count > 0 || field.Kind == FieldKind.Relation || field is { Kind: FieldKind.Component, Multiple: false });
        var name = filterable ? Claim(component.Name + "Filter", component.SchemaName) : null;
        _componentFilters[component.SchemaName] = name;
        if (name is not null)
        {
            _types.Add(InputOf(name, FilterFields(component)));
        }

        return name;
    }

    // A relation is filtered by the id of its target, or by the target's own fields under where.
    private string RelationFilter(FieldModel field)
    {
        var target = field.Target!;
        var key = $"{target.SchemaName}|{string.Join(',', field.Operators.Order())}";
        if (_relationFilters.TryGetValue(key, out var known))
        {
            return known;
        }

        var name = Claim(target.Name + "RelationFilter", target.SchemaName);
        _relationFilters[key] = name;
        var where = Filter(target);
        _types.Add(new InputObjectType(descriptor =>
        {
            descriptor.Name(name);
            OperatorFields(descriptor, ScalarNames.Id, field.Operators);
            descriptor.Field(FilterReader.Where).Type(Named(where)).Description($"Conditions on the fields of the {target.SchemaName} the relation points to.");
        }));
        return name;
    }

    // One input per operand and set of operators, named after the field type that first needs it.
    private string Operators(string owner, string operand, IEnumerable<FilterOperator> operators)
    {
        var set = operators.Order().ToList();
        var key = $"{operand}|{string.Join(',', set)}";
        if (_operatorInputs.TryGetValue(key, out var known))
        {
            return known;
        }

        var name = owner + "Operators";
        name = Claim(_operatorInputs.ContainsValue(name) ? owner + operand + "Operators" : name, owner);
        _operatorInputs[key] = name;
        _types.Add(new InputObjectType(descriptor =>
        {
            descriptor.Name(name);
            OperatorFields(descriptor, operand, set);
        }));
        return name;
    }

    private static void OperatorFields(IInputObjectTypeDescriptor descriptor, string operand, IEnumerable<FilterOperator> operators)
    {
        foreach (var filterOperator in operators.Order())
        {
            var field = descriptor.Field(FilterReader.NameOf(filterOperator));
            _ = filterOperator switch
            {
                FilterOperator.In or FilterOperator.NotIn or FilterOperator.Between => field.Type(ListOf(operand)),
                FilterOperator.IsNull or FilterOperator.IsNotNull => field.Type<BooleanType>(),
                FilterOperator.HasBlock => field.Type<StringType>(),
                _ => field.Type(Named(operand)),
            };
        }
    }

    // The sort keys of a type: its sortable system fields and fields, as the values of an enum.
    private (string Input, IReadOnlyDictionary<string, string> Paths) Sort(TypeModel type)
    {
        var paths = SystemFieldModel.All.Where(system => system.Sortable).Select(system => system.Name)
            .Concat(type.Fields.Where(field => field.Sortable).Select(field => field.Name))
            .ToImmutableSortedDictionary(name => GraphQLNames.EnumValue(name)!, name => name, StringComparer.Ordinal);

        var keys = Claim(type.Name + "SortField", type.SchemaName);
        _types.Add(new EnumType(descriptor =>
        {
            descriptor.Name(keys);
            foreach (var (name, path) in paths)
            {
                descriptor.Value(path).Name(name);
            }
        }));

        var input = Claim(type.Name + "Sort", type.SchemaName);
        _types.Add(new InputObjectType(descriptor =>
        {
            descriptor.Name(input);
            descriptor.Field(SortReader.FieldKey).Type(Required(Named(keys)));
            descriptor.Field(SortReader.DirectionKey).Type<EnumType<SortDirection>>().DefaultValue(SortDirection.Asc);
        }));
        return (input, paths);
    }

    // What a create or an update sends: every field a client may write, each optional, since a draft may leave any out.
    private string? Input(TypeModel type)
    {
        if (type.Role == TypeRole.Component && _componentInputs.TryGetValue(type.SchemaName, out var known))
        {
            return known;
        }

        var fields = type.Fields.Where(field => field.Writable).ToList();
        var name = fields.Count > 0 ? Claim(type.Name + "Input", type.SchemaName) : null;
        if (type.Role == TypeRole.Component)
        {
            _componentInputs[type.SchemaName] = name;
        }

        if (name is null)
        {
            return null;
        }

        // The inputs of the components come first, so that a component inside itself finds its own name.
        var types = fields.Select(field => (field.Name, Type: InputType(field))).Where(field => field.Type is not null).ToList();
        _types.Add(new InputObjectType(descriptor =>
        {
            descriptor.Name(name);
            foreach (var (field, fieldType) in types)
            {
                descriptor.Field(field).Type(fieldType!);
            }
        }));
        return name;
    }

    private ITypeNode? InputType(FieldModel field)
    {
        var name = field.Kind switch
        {
            FieldKind.Enum => field.TypeName!,
            FieldKind.Component => Input(field.Target!),

            // A relation and a media field are written as the ids of their targets, and a block list as JSON, since an
            // input cannot be a union: each block is an object with its type, as REST takes it.
            FieldKind.Relation or FieldKind.Reference => ScalarNames.Id,
            FieldKind.Blocks or FieldKind.Json or FieldKind.RichText => ScalarNames.Json,
            _ => field.Scalar,
        };

        return name is null ? null : field.Multiple && field.Kind is not (FieldKind.Json or FieldKind.RichText) ? ListOf(name) : Named(name);
    }
}
