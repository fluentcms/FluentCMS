using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Fields;

namespace FluentCMS.Admin.Content;

internal enum DiffKind
{
    Added,
    Removed,
    Changed,
    Moved,
}

internal enum DiffStepKind
{
    Field,
    Block,
    Item,
    Paragraph,
}

// One step of where a change is: a field by its description, a block by its type and position, an item or a paragraph of
// rich text by its position. Name is the key when the schema does not describe it.
internal sealed record DiffStep(DiffStepKind Kind, string Name, SchemaFieldDescription? Field = null, SchemaTypeDescription? Block = null, int Position = 0);

// A piece of a paragraph: the same words in both versions when Kind is null.
internal sealed record TextPiece(string Text, DiffKind? Kind);

// A row of a field's changes. A moved block has its positions; a changed paragraph its words; a change of rich text that
// left its text alone is Formatting.
internal sealed record DiffRow(IReadOnlyList<DiffStep> Where, DiffKind Kind, JsonNode? Before, JsonNode? After)
{
    public int FromPosition { get; init; }

    public int ToPosition { get; init; }

    public IReadOnlyList<TextPiece>? Words { get; init; }

    public bool Formatting { get; init; }
}

internal sealed record DiffField(SchemaFieldDescription Field, IReadOnlyList<DiffRow> Rows);

// The changes of a diff (spec 4.11) as rows per field, in the order of the type's fields. Paths are walked in the versions as
// read, so a block is named by its type and position; a block the server lists as added at one index and removed at another
// with the same id is one move; rich text, at any depth, is compared by the text of its paragraphs and their words. A field
// the type does not describe, which the schema leaves out when the caller may not read it, or that is hidden, is not shown.
internal static class VersionDiffRows
{
    private const string RichText = "richText";

    // Beyond these, a list is compared item for item rather than by the longest common run, which costs their product.
    private const int MaxParagraphCells = 1_000_000;
    private const int MaxWordCells = 250_000;

    public static IReadOnlyList<DiffField> Build(SchemaDescription schema, SchemaTypeDescription type, IReadOnlyList<VersionChangeInfo> changes, JsonObject? from, JsonObject? to)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(changes);

        List<DiffField> fields = [];
        foreach (var field in type.Fields.Where(field => !field.Hidden))
        {
            var own = changes.Where(change => Segments(change.Path) is [var first, ..] && first.Key == field.Name).ToList();
            if (own.Count == 0)
            {
                continue;
            }

            // Rows keep the order of the changes they come from; a rich text field's rows stand where its first change does.
            List<(int Order, DiffRow Row)> rows = [];
            HashSet<string> richTextDone = new(StringComparer.Ordinal);
            List<(VersionChangeInfo Change, List<DiffStep> Where, string Path, int Order)> plain = [];
            for (var order = 0; order < own.Count; order++)
            {
                var change = own[order];
                var walk = Walk(schema, field, Segments(change.Path), change.Kind == "removed" ? from : to);
                if (walk.RichText is { } prefix)
                {
                    if (richTextDone.Add(prefix.Path))
                    {
                        rows.AddRange(TextRows(prefix.Where, Locate(from, prefix.Path), Locate(to, prefix.Path)).Select(row => (order, row)));
                    }

                    continue;
                }

                plain.Add((change, walk.Where, change.Path, order));
            }

            rows.AddRange(PlainRows(plain));
            if (rows.Count > 0)
            {
                fields.Add(new DiffField(field, [.. rows.OrderBy(row => row.Order).Select(row => row.Row)]));
            }
        }

        return fields;
    }

    // The words of a value, as the diff shows a value that is not text: the text a block or a component holds, the plain
    // text of rich text, the items of a list.
    public static string Text(JsonNode? value) => value switch
    {
        null => "",
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => text,
        JsonValue scalar => scalar.ToJsonString(),
        JsonObject document when NestedValues.TypeOf(document) == "doc" => string.Join(" ", Paragraphs(document)),
        JsonObject item => string.Join(" · ", item.Where(member => member.Key is not ("id" or "type")).Select(member => Text(member.Value)).Where(text => text.Length > 0)),
        JsonArray items => string.Join(", ", items.Select(Text).Where(text => text.Length > 0)),
        _ => value.ToJsonString(),
    };

    private sealed record Segment(string Key, int Index)
    {
        public bool IsIndex => Index >= 0;
    }

    private sealed record WalkResult(List<DiffStep> Where, (string Path, List<DiffStep> Where)? RichText);

    private static List<Segment> Segments(string path)
    {
        List<Segment> segments = [];
        var index = 0;
        while (index < path.Length)
        {
            if (path[index] == '[')
            {
                var end = path.IndexOf(']', index);
                if (end < 0 || !int.TryParse(path.AsSpan(index + 1, end - index - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    break;
                }

                segments.Add(new Segment("", number));
                index = end + 1;
            }
            else
            {
                if (path[index] == '.')
                {
                    index++;
                }

                var end = path.IndexOfAny(['.', '['], index);
                end = end < 0 ? path.Length : end;
                segments.Add(new Segment(path[index..end], -1));
                index = end;
            }
        }

        return segments;
    }

    private static string PathOf(IEnumerable<Segment> segments)
    {
        var path = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.IsIndex)
            {
                path.Append(CultureInfo.InvariantCulture, $"[{segment.Index}]");
            }
            else
            {
                path.Append(path.Length == 0 ? "" : ".").Append(segment.Key);
            }
        }

        return path.ToString();
    }

    // The steps of a path, named from the schema and the version it is read in. A rich text field ends the walk: its changes
    // are shown together, by text.
    private static WalkResult Walk(SchemaDescription schema, SchemaFieldDescription field, List<Segment> segments, JsonObject? document)
    {
        List<DiffStep> where = [];
        JsonNode? node = document?[field.Name];
        SchemaFieldDescription? current = field;
        SchemaTypeDescription? holder = null;
        for (var index = 1; index <= segments.Count; index++)
        {
            if (current?.Type == RichText)
            {
                return new WalkResult(where, (PathOf(segments.Take(index)), [.. where]));
            }

            if (index == segments.Count)
            {
                break;
            }

            var segment = segments[index];
            if (segment.IsIndex)
            {
                node = node is JsonArray list && segment.Index < list.Count ? list[segment.Index] : null;
                var position = segment.Index + 1;

                // A block names itself, so the list it is in is not named before it: Section 1 › Card 2.
                if (current?.Type is "blocks" or "component" && where.Count > 0 && where[^1].Field == current)
                {
                    where.RemoveAt(where.Count - 1);
                }

                if (current?.Type == "blocks" && schema.Blocks.FirstOrDefault(block => block.Name == NestedValues.TypeOf(node)) is { } block)
                {
                    where.Add(new DiffStep(DiffStepKind.Block, block.Name, Block: block, Position: position));
                    holder = block;
                }
                else if (current?.Type == "component" && NestedValues.ComponentOf(schema, current) is { } component)
                {
                    where.Add(new DiffStep(DiffStepKind.Block, component.Name, Block: component, Position: position));
                    holder = component;
                }
                else
                {
                    where.Add(new DiffStep(DiffStepKind.Item, "", Position: position));
                    holder = null;
                }

                current = null;
                continue;
            }

            if (current?.Type == "component" && !current.Multiple)
            {
                holder = NestedValues.ComponentOf(schema, current);
            }

            node = node is JsonObject item ? item[segment.Key] : null;
            current = holder?.Fields.FirstOrDefault(nested => nested.Name == segment.Key);
            where.Add(current is null ? new DiffStep(DiffStepKind.Field, segment.Key) : new DiffStep(DiffStepKind.Field, segment.Key, Field: current));
        }

        return new WalkResult(where, null);
    }

    private static JsonNode? Locate(JsonObject? document, string path)
    {
        JsonNode? node = document;
        foreach (var segment in Segments(path))
        {
            node = segment.IsIndex
                ? node is JsonArray list && segment.Index < list.Count ? list[segment.Index] : null
                : node is JsonObject item ? item[segment.Key] : null;
        }

        return node;
    }

    // An item with an id added at one index and removed at another of the same list is a move; its own changes, if it has
    // any, are listed under it by its fields.
    private static IEnumerable<(int Order, DiffRow Row)> PlainRows(List<(VersionChangeInfo Change, List<DiffStep> Where, string Path, int Order)> changes)
    {
        var removed = changes
            .Where(item => item.Change.Kind == "removed" && NestedValues.IdOf(item.Change.Before) is not null)
            .ToDictionary(item => (ListOf(item.Path), NestedValues.IdOf(item.Change.Before)!), item => item);
        HashSet<string> moved = new(StringComparer.Ordinal);
        foreach (var (change, where, path, order) in changes)
        {
            var kind = change.Kind switch
            {
                "added" => DiffKind.Added,
                "removed" => DiffKind.Removed,
                _ => DiffKind.Changed,
            };

            if (kind == DiffKind.Added && NestedValues.IdOf(change.After) is { } id && removed.TryGetValue((ListOf(path), id), out var old))
            {
                moved.Add(old.Path);
                yield return (order, new DiffRow(where, DiffKind.Moved, null, null) { FromPosition = PositionOf(old.Path), ToPosition = PositionOf(path) });
                foreach (var row in MovedChanges(where, old.Change.Before as JsonObject, change.After as JsonObject))
                {
                    yield return (order, row);
                }

                continue;
            }

            if (kind == DiffKind.Removed && moved.Contains(path))
            {
                continue;
            }

            if (kind == DiffKind.Removed && NestedValues.IdOf(change.Before) is { } gone
                && changes.Any(item => item.Change.Kind == "added" && NestedValues.IdOf(item.Change.After) == gone && ListOf(item.Path) == ListOf(path)))
            {
                continue;
            }

            yield return (order, new DiffRow(where, kind, change.Before, change.After));
        }
    }

    // The fields of a moved block that differ, compared here since the server lists the block whole.
    private static IEnumerable<DiffRow> MovedChanges(List<DiffStep> where, JsonObject? before, JsonObject? after)
    {
        if (before is null || after is null)
        {
            yield break;
        }

        var block = where.LastOrDefault()?.Block;
        foreach (var key in before.Select(member => member.Key).Union(after.Select(member => member.Key)).Where(key => key is not ("id" or "type")))
        {
            if (JsonNode.DeepEquals(before[key], after[key]))
            {
                continue;
            }

            var field = block?.Fields.FirstOrDefault(nested => nested.Name == key);
            DiffStep step = field is null ? new DiffStep(DiffStepKind.Field, key) : new DiffStep(DiffStepKind.Field, key, Field: field);
            var kind = before[key] is null ? DiffKind.Added : after[key] is null ? DiffKind.Removed : DiffKind.Changed;
            if (field?.Type == RichText)
            {
                foreach (var row in TextRows([.. where, step], before[key], after[key]))
                {
                    yield return row;
                }
            }
            else
            {
                yield return new DiffRow([.. where, step], kind, before[key], after[key]);
            }
        }
    }

    private static string ListOf(string path) => path.LastIndexOf('[') is var at and > 0 ? path[..at] : path;

    private static int PositionOf(string path) =>
        path.LastIndexOf('[') is var at and >= 0 && int.TryParse(path.AsSpan(at + 1, path.Length - at - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index + 1
            : 0;

    // Rich text by its text: the paragraphs of both versions paired by the longest run they share, a changed one by its words.
    private static IEnumerable<DiffRow> TextRows(List<DiffStep> where, JsonNode? before, JsonNode? after)
    {
        var left = before is JsonObject a ? Paragraphs(a) : [];
        var right = after is JsonObject b ? Paragraphs(b) : [];
        var any = false;
        foreach (var (kind, leftIndex, rightIndex) in Align(left, right, MaxParagraphCells))
        {
            if (kind is null)
            {
                continue;
            }

            any = true;
            var position = kind == DiffKind.Removed ? leftIndex + 1 : rightIndex + 1;
            List<DiffStep> at = [.. where, new DiffStep(DiffStepKind.Paragraph, "", Position: position)];
            yield return kind switch
            {
                DiffKind.Added => new DiffRow(at, DiffKind.Added, null, right[rightIndex]),
                DiffKind.Removed => new DiffRow(at, DiffKind.Removed, left[leftIndex], null),
                _ => new DiffRow(at, DiffKind.Changed, left[leftIndex], right[rightIndex]) { Words = Words(left[leftIndex], right[rightIndex]) },
            };
        }

        if (!any && !JsonNode.DeepEquals(before, after))
        {
            yield return new DiffRow(where, DiffKind.Changed, null, null) { Formatting = true };
        }
    }

    // The text of each top-level node; a list or a quote is one paragraph of all it holds.
    private static List<string> Paragraphs(JsonObject document) =>
        document["content"] is JsonArray content ? [.. content.Select(node => Flatten(node).Trim())] : [];

    private static string Flatten(JsonNode? node)
    {
        if (node is not JsonObject item)
        {
            return "";
        }

        if (NestedValues.Text(item, "text") is { } text)
        {
            return text;
        }

        var parts = item["content"] is JsonArray children ? children.Select(Flatten).ToList() : [];
        var block = NestedValues.TypeOf(item) is "paragraph" or "heading" ? "" : " ";
        return string.Join(block, parts);
    }

    // Each step pairs an item of left with one of right (kind null, the same), takes one from left (removed) or one from
    // right (added); a removed item next to an added one is one changed item.
    private static List<(DiffKind? Kind, int Left, int Right)> Align(List<string> left, List<string> right, int maxCells)
    {
        List<(DiffKind? Kind, int Left, int Right)> steps = [];
        if ((long)left.Count * right.Count > maxCells)
        {
            for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
            {
                steps.Add(index >= left.Count ? (DiffKind.Added, -1, index) : index >= right.Count ? (DiffKind.Removed, index, -1)
                    : (string.Equals(left[index], right[index], StringComparison.Ordinal) ? null : DiffKind.Changed, index, index));
            }

            return steps;
        }

        var lengths = new int[left.Count + 1, right.Count + 1];
        for (var i = left.Count - 1; i >= 0; i--)
        {
            for (var j = right.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        int l = 0, r = 0;
        while (l < left.Count || r < right.Count)
        {
            if (l < left.Count && r < right.Count && string.Equals(left[l], right[r], StringComparison.Ordinal))
            {
                steps.Add((null, l++, r++));
            }
            else if (r < right.Count && (l == left.Count || lengths[l, r + 1] >= lengths[l + 1, r]))
            {
                steps.Add((DiffKind.Added, -1, r++));
            }
            else
            {
                steps.Add((DiffKind.Removed, l++, -1));
            }
        }

        return Pair(steps);
    }

    // A run of removed items followed by a run of added ones becomes changed items, one for one.
    private static List<(DiffKind? Kind, int Left, int Right)> Pair(List<(DiffKind? Kind, int Left, int Right)> steps)
    {
        List<(DiffKind? Kind, int Left, int Right)> paired = [];
        var index = 0;
        while (index < steps.Count)
        {
            if (steps[index].Kind is not (DiffKind.Removed or DiffKind.Added))
            {
                paired.Add(steps[index++]);
                continue;
            }

            List<int> gone = [], come = [];
            while (index < steps.Count && steps[index].Kind is DiffKind.Removed or DiffKind.Added)
            {
                if (steps[index].Kind == DiffKind.Removed)
                {
                    gone.Add(steps[index].Left);
                }
                else
                {
                    come.Add(steps[index].Right);
                }

                index++;
            }

            for (var k = 0; k < Math.Max(gone.Count, come.Count); k++)
            {
                paired.Add(k < gone.Count && k < come.Count ? (DiffKind.Changed, gone[k], come[k]) : k < gone.Count ? (DiffKind.Removed, gone[k], -1) : (DiffKind.Added, -1, come[k]));
            }
        }

        return paired;
    }

    // The words of a changed paragraph, each with the whitespace after it, marked as kept, removed or added.
    private static List<TextPiece> Words(string before, string after)
    {
        var left = Tokens(before);
        var right = Tokens(after);
        if ((long)left.Count * right.Count > MaxWordCells)
        {
            return [new TextPiece(before, DiffKind.Removed), new TextPiece(after, DiffKind.Added)];
        }

        List<TextPiece> pieces = [];
        foreach (var (kind, l, r) in Align(left, right, MaxWordCells))
        {
            switch (kind)
            {
                case null:
                    Append(pieces, left[l], null);
                    break;
                case DiffKind.Changed:
                    Append(pieces, left[l], DiffKind.Removed);
                    Append(pieces, right[r], DiffKind.Added);
                    break;
                case DiffKind.Removed:
                    Append(pieces, left[l], DiffKind.Removed);
                    break;
                default:
                    Append(pieces, right[r], DiffKind.Added);
                    break;
            }
        }

        return pieces;
    }

    private static void Append(List<TextPiece> pieces, string text, DiffKind? kind)
    {
        if (pieces.Count > 0 && pieces[^1].Kind == kind)
        {
            pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + text };
        }
        else
        {
            pieces.Add(new TextPiece(text, kind));
        }
    }

    private static List<string> Tokens(string text)
    {
        List<string> tokens = [];
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]) && (index + 1 == text.Length || !char.IsWhiteSpace(text[index + 1])))
            {
                tokens.Add(text[start..(index + 1)]);
                start = index + 1;
            }
        }

        if (start < text.Length)
        {
            tokens.Add(text[start..]);
        }

        return tokens;
    }
}
