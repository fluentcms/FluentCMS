using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

public static class RichTextValidator
{
    public static IReadOnlyList<RichTextValidationError> Validate(RichText value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return RichTextParser.TryParse(value, out _, out var errors) ? [] : errors;
    }

    public static IReadOnlyList<RichTextValidationError> Validate(DocumentNode document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<RichTextValidationError>();
        ValidateChildren(document, "$", errors);
        return errors;
    }

    private static void ValidateChildren(RichTextNode parent, string path, List<RichTextValidationError> errors)
    {
        for (var index = 0; index < parent.Content.Count; index++)
        {
            var child = parent.Content[index];
            var childPath = $"{path}.content[{index}]";

            if (!IsAllowedIn(parent.Spec.Content, child))
            {
                errors.Add(new RichTextValidationError(childPath, $"A '{child.Type}' node is not allowed inside a '{parent.Type}' node."));
                continue;
            }

            ValidateMarks(child, parent.Spec.Content, childPath, errors);
            ValidateChildren(child, childPath, errors);
        }
    }

    private static bool IsAllowedIn(ContentKind content, RichTextNode child) =>
        content switch
        {
            ContentKind.Blocks => child.Spec.Position is NodePosition.Block or NodePosition.BlockOrInline,
            ContentKind.Inlines => child.Spec.Position is NodePosition.Inline or NodePosition.BlockOrInline,
            ContentKind.ListItems => child.Spec.Position == NodePosition.ListItem,
            ContentKind.Text => child.Spec == NodeSpec.Text,
            _ => false,
        };

    private static void ValidateMarks(RichTextNode node, ContentKind parentContent, string path, List<RichTextValidationError> errors)
    {
        if (node.Marks.Count == 0)
        {
            return;
        }

        if (parentContent != ContentKind.Inlines)
        {
            errors.Add(new RichTextValidationError($"{path}.marks", $"A '{node.Type}' node cannot have marks here; marks apply to inline content only."));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < node.Marks.Count; index++)
        {
            if (!seen.Add(node.Marks[index].Type))
            {
                errors.Add(new RichTextValidationError($"{path}.marks[{index}]", $"The '{node.Marks[index].Type}' mark appears more than once."));
            }
        }
    }
}
