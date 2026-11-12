using System.Text;

namespace FluentCMS.Generators.Emit;

// Emits the typed filter and sort builders: a static {Type}Fields class per collection and singleton, and a class per
// component ({Name}ComponentFields) and block type ({Name}BlockFields) that a field reaches by path. The text depends on
// the descriptions and the options alone, so the source generator and the tool emit the same code for the same types.
internal static class FieldsEmitter
{
    // A member with one of these names would hide or clash with a member every builder has.
    private static readonly string[] ObjectMembers = ["Equals", "GetHashCode", "ToString", "GetType", "ReferenceEquals", "MemberwiseClone", "Finalize"];

    private static readonly string[] BlocksFieldMembers = ["Path", "HasBlock", "IsNull", "IsNotNull"];

    private static readonly HashSet<string> KnownFieldTypes = new(StringComparer.Ordinal)
    {
        "text", "longText", "richText", "integer", "decimal", "boolean", "date", "dateTime", "email", "url", "slug",
        "select", "json", "relation", "component", "blocks", "media",
    };

    public static EmitResult Emit(IEnumerable<TypeDescription> types, EmitOptions options) => new Run(options).Emit(types);

    private enum Group
    {
        ContentType,
        Component,
        Block,
    }

    private static Group GroupOf(BuilderKind kind) => kind switch
    {
        BuilderKind.Component => Group.Component,
        BuilderKind.Block => Group.Block,
        _ => Group.ContentType,
    };

    private static string Label(BuilderKind kind) => kind switch
    {
        BuilderKind.Collection => "collection",
        BuilderKind.Singleton => "singleton",
        BuilderKind.Component => "component",
        _ => "block type",
    };

    // Type names are lower kebab-case and field names camelCase (spec 3.3), so a name that passes gives an identifier.
    internal static bool IsTypeName(string name)
    {
        if (name.Length == 0 || !IsLower(name[0]) || name[name.Length - 1] == '-')
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '-' ? name[i - 1] == '-' : !IsLower(c) && !IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsFieldName(string name) =>
        name.Length > 0 && IsLower(name[0]) && name.All(c => IsLower(c) || IsUpper(c) || IsDigit(c));

    internal static string Pascal(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var part in name.Split('-'))
        {
            if (part.Length > 0)
            {
                builder.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
            }
        }

        return builder.ToString();
    }

    private static bool IsLower(char c) => c is >= 'a' and <= 'z';

    private static bool IsUpper(char c) => c is >= 'A' and <= 'Z';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private sealed class Builder(TypeDescription type, string className)
    {
        public TypeDescription Type { get; } = type;

        public string ClassName { get; } = className;

        public string FullName => Type.Namespace.Length == 0 ? $"global::{ClassName}" : $"global::{Type.Namespace}.{ClassName}";

        public string HintName => Type.Namespace.Length == 0 ? $"{ClassName}.g.cs" : $"{Type.Namespace}.{ClassName}.g.cs";
    }

    private sealed class Run(EmitOptions options)
    {
        private readonly string _fields = "global::" + options.FieldTypesNamespace + ".";
        private readonly Dictionary<(Group, string), Builder> _builders = [];
        private readonly List<EmitDiagnostic> _diagnostics = [];

        public EmitResult Emit(IEnumerable<TypeDescription> types)
        {
            var ordered = types
                .OrderBy(type => GroupOf(type.Kind))
                .ThenBy(type => type.Name, StringComparer.Ordinal)
                .ThenBy(type => type.Namespace, StringComparer.Ordinal)
                .ToList();

            List<Builder> builders = [];
            var classNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in ordered)
            {
                var group = GroupOf(type.Kind);
                if (!IsTypeName(type.Name))
                {
                    Report(EmitCodes.TypeSkipped, $"The {Label(type.Kind)} '{type.Name}' gets no field builder, because its name gives no C# class name.", type, null);
                    continue;
                }

                var className = Pascal(type.Name) + group switch
                {
                    Group.Component => "ComponentFields",
                    Group.Block => "BlockFields",
                    _ => "Fields",
                };

                // A filter path names the block type as a segment, which may hold only letters, digits and underscores.
                if (group == Group.Block && type.Name.Contains('-'))
                {
                    Report(EmitCodes.BlockNotReachable, $"The block type '{type.Name}' gets no field builder, because a filter path cannot name a block type with a hyphen; a block list still has Has{Pascal(type.Name)}().", type, null);
                    continue;
                }

                var builder = new Builder(type, className);
                if (_builders.ContainsKey((group, type.Name)) || !classNames.Add(builder.FullName))
                {
                    Report(EmitCodes.TypeSkipped, $"The {Label(type.Kind)} '{type.Name}' gets no field builder, because {className} is the builder of another type.", type, null);
                    continue;
                }

                _builders[(group, type.Name)] = builder;
                builders.Add(builder);
            }

            var sources = builders.Select(builder => new EmittedSource(builder.HintName, Write(builder))).ToEquatableArray();
            return new EmitResult(sources, _diagnostics.ToEquatableArray());
        }

        private void Report(string code, string message, TypeDescription type, string? field) =>
            _diagnostics.Add(new EmitDiagnostic(code, message, type.Kind, type.Name, type.Namespace, field));

        private string Write(Builder builder)
        {
            var type = builder.Type;
            var writer = new CodeWriter();
            writer.Line("// <auto-generated/>");
            writer.Line("#nullable enable");
            writer.Line();

            if (type.Namespace.Length > 0)
            {
                writer.Line($"namespace {type.Namespace}");
                writer.Open();
            }

            switch (GroupOf(type.Kind))
            {
                case Group.ContentType:
                    writer.Line($"/// <summary>Typed filters and sorts on the fields of the {Label(type.Kind)} <c>{type.Name}</c>.</summary>");
                    writer.Line($"public static class {builder.ClassName}");
                    writer.Open();
                    WriteMembers(writer, builder, isStatic: true);
                    writer.Close();
                    break;
                case Group.Component:
                    writer.Line($"/// <summary>Typed filters and sorts on the fields of the component <c>{type.Name}</c>, below the field that holds it.</summary>");
                    writer.Line($"public sealed class {builder.ClassName}");
                    writer.Open();
                    WriteConstructor(writer, builder.ClassName, "Creates the builder for a field that holds the component.", "The path of the field.");
                    WriteMembers(writer, builder, isStatic: false);
                    writer.Close();
                    break;
                default:
                    writer.Line($"/// <summary>Typed filters on the fields of the block type <c>{type.Name}</c> in a block list, one level deep.</summary>");
                    writer.Line($"public sealed class {builder.ClassName}");
                    writer.Open();
                    WriteConstructor(writer, builder.ClassName, "Creates the builder for the blocks of this type in a block list.", "The path of the block list.");
                    WriteBlockMembers(writer, builder);
                    writer.Close();
                    break;
            }

            if (type.Namespace.Length > 0)
            {
                writer.Close();
            }

            return writer.ToString();
        }

        private static void WriteConstructor(CodeWriter writer, string className, string summary, string parameter)
        {
            writer.Line("private readonly string _path;");
            writer.Line();
            writer.Line($"/// <summary>{summary}</summary>");
            writer.Line($"/// <param name=\"path\">{parameter}</param>");
            writer.Line($"public {className}(string path)");
            writer.Open();
            writer.Line("_path = path;");
            writer.Close();
        }

        private void WriteMembers(CodeWriter writer, Builder builder, bool isStatic)
        {
            var type = builder.Type;
            var used = new HashSet<string>(ObjectMembers, StringComparer.Ordinal) { builder.ClassName };
            List<(FieldDescription Field, string ClassName)> blockLists = [];
            var first = isStatic;

            foreach (var field in type.Fields)
            {
                if (!IsFieldName(field.Name))
                {
                    continue;
                }

                var member = Pascal(field.Name);
                var path = isStatic ? Literal(field.Name) : "_path + " + Literal("." + field.Name);
                string fieldType;
                string summary = $"The field <c>{field.Name}</c> ({field.FieldType}{(field.Multiple ? ", multiple" : "")}).";

                switch (field.FieldType)
                {
                    case "blocks":
                        fieldType = member + "Blocks";
                        if (!Claim(used, member, fieldType))
                        {
                            Report(EmitCodes.MemberNameTaken, $"The field '{type.Name}.{field.Name}' gets no builder, because {member} or {fieldType} is taken in {builder.ClassName}.", type, field.Name);
                            continue;
                        }

                        blockLists.Add((field, fieldType));
                        break;
                    case "component" when field.Multiple:
                        // A repeated component is stored as JSON that a filter cannot reach.
                        continue;
                    case "component":
                        if (field.Component is null || !_builders.TryGetValue((Group.Component, field.Component), out var component))
                        {
                            Report(EmitCodes.MissingBuilder, $"The field '{type.Name}.{field.Name}' gets no builder, because the component '{field.Component}' has none here; declare it in the same project.", type, field.Name);
                            continue;
                        }

                        fieldType = component.FullName;
                        if (!Claim(used, member))
                        {
                            Report(EmitCodes.MemberNameTaken, $"The field '{type.Name}.{field.Name}' gets no builder, because {member} is taken in {builder.ClassName}.", type, field.Name);
                            continue;
                        }

                        break;
                    default:
                        if (FieldTypeOf(field) is not { } known)
                        {
                            if (!KnownFieldTypes.Contains(field.FieldType))
                            {
                                Report(EmitCodes.UnknownFieldType, $"The field '{type.Name}.{field.Name}' gets no builder, because the field type '{field.FieldType}' has none.", type, field.Name);
                            }

                            continue;
                        }

                        fieldType = known;
                        if (!Claim(used, member))
                        {
                            Report(EmitCodes.MemberNameTaken, $"The field '{type.Name}.{field.Name}' gets no builder, because {member} is taken in {builder.ClassName}.", type, field.Name);
                            continue;
                        }

                        break;
                }

                if (!first)
                {
                    writer.Line();
                }

                first = false;
                writer.Line($"/// <summary>{summary}</summary>");
                writer.Line(isStatic
                    ? $"public static {fieldType} {member} {{ get; }} = new {fieldType}({path});"
                    : $"public {fieldType} {member} => new {fieldType}({path});");
            }

            foreach (var (field, className) in blockLists)
            {
                writer.Line();
                WriteBlockList(writer, type, field, className);
            }
        }

        private void WriteBlockList(CodeWriter writer, TypeDescription owner, FieldDescription field, string className)
        {
            writer.Line($"/// <summary>The block types of the field <c>{field.Name}</c>, and the presence of each in the list.</summary>");
            writer.Line($"public sealed class {className} : {_fields}BlocksField");
            writer.Open();
            writer.Line("/// <summary>Creates the builder of the field at a path.</summary>");
            writer.Line("/// <param name=\"path\">The path of the field.</param>");
            writer.Line($"public {className}(string path)");
            writer.Line("    : base(path)");
            writer.Open();
            writer.Close();

            var used = new HashSet<string>(ObjectMembers.Concat(BlocksFieldMembers), StringComparer.Ordinal) { className };
            foreach (var block in field.Blocks)
            {
                if (!IsTypeName(block))
                {
                    continue;
                }

                var member = Pascal(block);
                var has = "Has" + member;
                var hasBuilder = _builders.TryGetValue((Group.Block, block), out var builder);
                if (!(hasBuilder ? Claim(used, member, has) : Claim(used, has)))
                {
                    Report(EmitCodes.MemberNameTaken, $"The block type '{block}' of the field '{owner.Name}.{field.Name}' gets no member, because {member} or {has} is taken in {className}.", owner, field.Name);
                    continue;
                }

                if (hasBuilder)
                {
                    writer.Line();
                    writer.Line($"/// <summary>The fields of the blocks of the type <c>{block}</c> in this list.</summary>");
                    writer.Line($"public {builder!.FullName} {member} => new {builder.FullName}(Path);");
                }
                else if (!block.Contains('-'))
                {
                    Report(EmitCodes.MissingBuilder, $"The block type '{block}' of the field '{owner.Name}.{field.Name}' has no builder here, so only {has}() is emitted; declare it in the same project.", owner, field.Name);
                }

                writer.Line();
                writer.Line($"/// <summary>The list holds a block of the type <c>{block}</c> (<c>hasBlock</c>).</summary>");
                writer.Line("/// <returns>The filter.</returns>");
                writer.Line($"public {_fields}FieldFilter {has}() => HasBlock({Literal(block)});");
            }

            writer.Close();
        }

        private void WriteBlockMembers(CodeWriter writer, Builder builder)
        {
            var type = builder.Type;
            var used = new HashSet<string>(ObjectMembers, StringComparer.Ordinal) { builder.ClassName };
            foreach (var field in type.Fields)
            {
                if (!IsFieldName(field.Name))
                {
                    continue;
                }

                if (BlockFieldTypeOf(field) is not { } fieldType)
                {
                    // Q7: a filter reaches only a scalar one level inside a block, so other fields have no member.
                    if (!KnownFieldTypes.Contains(field.FieldType))
                    {
                        Report(EmitCodes.UnknownFieldType, $"The field '{type.Name}.{field.Name}' gets no builder, because the field type '{field.FieldType}' has none.", type, field.Name);
                    }

                    continue;
                }

                var member = Pascal(field.Name);
                if (!Claim(used, member))
                {
                    Report(EmitCodes.MemberNameTaken, $"The field '{type.Name}.{field.Name}' gets no builder, because {member} is taken in {builder.ClassName}.", type, field.Name);
                    continue;
                }

                writer.Line();
                writer.Line($"/// <summary>The field <c>{field.Name}</c> ({field.FieldType}{(field.Multiple ? ", multiple" : "")}) of the blocks of this type.</summary>");
                writer.Line($"public {fieldType} {member} => new {fieldType}(_path + {Literal("." + type.Name + "." + field.Name)});");
            }
        }

        private string? FieldTypeOf(FieldDescription field) => field.FieldType switch
        {
            "text" or "slug" or "email" or "url" => _fields + "TextField",
            "longText" => _fields + "LongTextField",
            "richText" => _fields + "RichTextField",
            "integer" => _fields + "NumberField<long>",
            "decimal" => _fields + "NumberField<decimal>",
            "boolean" => _fields + "BooleanField",
            "date" => _fields + "DateField<global::System.DateOnly>",
            "dateTime" => _fields + "DateField<global::System.DateTimeOffset>",
            "select" => _fields + (field.Multiple ? "SelectListField" : "SelectField"),
            "json" => _fields + "JsonField",
            "relation" => _fields + "RelationField",
            "media" => _fields + "MediaField",
            _ => null,
        };

        private string? BlockFieldTypeOf(FieldDescription field) => field.FieldType switch
        {
            "text" or "slug" or "email" or "url" => _fields + "BlockTextField",
            "select" => _fields + (field.Multiple ? "BlockContainsField" : "BlockTextField"),
            "longText" => _fields + "BlockContainsField",
            "integer" => _fields + "BlockValueField<long>",
            "decimal" => _fields + "BlockValueField<decimal>",
            "boolean" => _fields + "BlockValueField<bool>",
            "date" => _fields + "BlockValueField<global::System.DateOnly>",
            "dateTime" => _fields + "BlockValueField<global::System.DateTimeOffset>",
            _ => null,
        };

        private static bool Claim(HashSet<string> used, params string[] names)
        {
            if (names.Any(used.Contains))
            {
                return false;
            }

            foreach (var name in names)
            {
                used.Add(name);
            }

            return true;
        }
    }

    private sealed class CodeWriter
    {
        private readonly StringBuilder _text = new();
        private int _indent;

        public void Line(string line = "")
        {
            if (line.Length > 0)
            {
                _text.Append(' ', _indent * 4).Append(line);
            }

            _text.Append('\n');
        }

        public void Open()
        {
            Line("{");
            _indent++;
        }

        public void Close()
        {
            _indent--;
            Line("}");
        }

        public override string ToString() => _text.ToString();
    }
}
