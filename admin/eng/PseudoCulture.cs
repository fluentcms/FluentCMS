// The pseudo-cultures of the admin (AQ14): every value of a resource comes out marked, so a visible text that is not marked
// did not come from a resource. One file for the build, whose inline tasks compile it (admin/src/Directory.Build.targets),
// and for the tests, which compile it with PSEUDO_CULTURE_MARKING_ONLY and leave the tasks out.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FluentCMS.Admin.PseudoLocalization
{
    internal static partial class PseudoCulture
    {
        public const string LeftToRight = "en-XA";
        public const string RightToLeft = "ar-XB";

        // Brackets a developer does not type by accident, so that a hard-coded "[Draft]" is not taken for a resource.
        public const char Open = '⟦';
        public const char Close = '⟧';
        private const char RightToLeftOverride = '‮';
        private const char PopDirectionalFormatting = '‬';

        public static readonly string[] Names = { LeftToRight, RightToLeft };

        public static bool IsRightToLeft(string culture) => string.Equals(culture, RightToLeft, StringComparison.OrdinalIgnoreCase);

        public static string Transform(string value, bool rightToLeft) =>
            rightToLeft
                ? $"{Open}{RightToLeftOverride}{value}{PopDirectionalFormatting}{Close}"
                : $"{Open}{Accent(value)}{Close}";

        public static string Transform(string value, string culture) => Transform(value, IsRightToLeft(culture));

        // A resource file of the culture from the neutral one: each text marked, every other entry as it is.
        public static XDocument Resources(XDocument neutral, string culture)
        {
            var document = new XDocument(neutral);
            foreach (var data in document.Root!.Elements("data").Where(IsText))
            {
                var value = data.Element("value");
                if (value is not null)
                {
                    value.Value = Transform(value.Value, culture);
                }

                data.Element("comment")?.Remove();
            }

            return document;
        }

        // A string entry: one with neither a type nor a mime type.
        public static bool IsText(XElement data) => data.Attribute("type") is null && data.Attribute("mimetype") is null;

        // Accents the letters, leaving format items such as {0} as they are.
        private static string Accent(string value)
        {
            var builder = new StringBuilder(value.Length);
            var depth = 0;
            foreach (var c in value)
            {
                depth += c == '{' ? 1 : c == '}' ? -1 : 0;
                builder.Append(depth > 0 ? c : c switch
                {
                    'a' => 'à', 'e' => 'é', 'i' => 'î', 'o' => 'ö', 'u' => 'ü', 'c' => 'ç', 'n' => 'ñ', 's' => 'š', 'y' => 'ý',
                    'A' => 'À', 'E' => 'É', 'I' => 'Î', 'O' => 'Ö', 'U' => 'Ü', 'C' => 'Ç', 'N' => 'Ñ', 'S' => 'Š', 'Y' => 'Ý',
                    _ => c,
                });
            }

            return builder.ToString();
        }
    }

    // The menu texts of the gathered manifests of a build with the pseudo-cultures: each "labels" object that has an "en"
    // text and none of a pseudo-culture gets one, marked from it. The rest of the text, comments included, is kept as it is.
    internal static class PseudoManifestLabels
    {
        public static string Mark(string json)
        {
            var output = new StringBuilder(json.Length + 256);
            var position = 0;
            while (position < json.Length)
            {
                var c = json[position];
                if (c == '"')
                {
                    var end = StringEnd(json, position);
                    var name = Decode(json, position, end);
                    output.Append(json, position, end - position);
                    position = end;
                    var colon = SkipSpace(json, position);
                    if (name == "labels" && colon < json.Length && json[colon] == ':')
                    {
                        var open = SkipSpace(json, colon + 1);
                        if (open < json.Length && json[open] == '{')
                        {
                            output.Append(json, position, open + 1 - position);
                            position = Labels(json, open + 1, output);
                        }
                    }
                }
                else if (c == '/' && position + 1 < json.Length && (json[position + 1] == '/' || json[position + 1] == '*'))
                {
                    var end = CommentEnd(json, position);
                    output.Append(json, position, end - position);
                    position = end;
                }
                else
                {
                    output.Append(c);
                    position++;
                }
            }

            return output.ToString();
        }

        // Copies the members of a labels object up to its closing brace, and adds the pseudo-cultures' texts before it.
        private static int Labels(string json, int position, StringBuilder output)
        {
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var start = position;
            var lastSignificant = '{';
            var lineComment = false;
            while (position < json.Length && json[position] != '}')
            {
                var c = json[position];
                if (c == '"')
                {
                    var keyEnd = StringEnd(json, position);
                    var key = Decode(json, position, keyEnd);
                    var colon = SkipSpace(json, keyEnd);
                    var valueStart = SkipSpace(json, colon + 1);
                    if (colon < json.Length && json[colon] == ':' && valueStart < json.Length && json[valueStart] == '"')
                    {
                        var valueEnd = StringEnd(json, valueStart);
                        texts[key] = Decode(json, valueStart, valueEnd);
                        position = valueEnd;
                    }
                    else
                    {
                        position = keyEnd;
                    }

                    lastSignificant = '"';
                    lineComment = false;
                }
                else if (c == '/' && position + 1 < json.Length && (json[position + 1] == '/' || json[position + 1] == '*'))
                {
                    lineComment = json[position + 1] == '/';
                    position = CommentEnd(json, position);
                }
                else
                {
                    if (!char.IsWhiteSpace(c))
                    {
                        lastSignificant = c;
                        lineComment = false;
                    }

                    position++;
                }
            }

            // The space before the closing brace stays after the added texts, unless a line comment ends the members.
            var members = json.Substring(start, position - start);
            var body = lineComment ? members : members.TrimEnd();
            output.Append(body);
            if (texts.TryGetValue("en", out var neutral) && !PseudoCulture.Names.Any(texts.ContainsKey))
            {
                output.Append(lastSignificant is '{' or ',' ? " " : ", ")
                    .Append(string.Join(", ", PseudoCulture.Names.Select(culture => $"{Encode(culture)}: {Encode(PseudoCulture.Transform(neutral, culture))}")));
            }

            output.Append(members, body.Length, members.Length - body.Length);

            return position;
        }

        private static int SkipSpace(string json, int position)
        {
            while (position < json.Length)
            {
                if (char.IsWhiteSpace(json[position]))
                {
                    position++;
                }
                else if (json[position] == '/' && position + 1 < json.Length && (json[position + 1] == '/' || json[position + 1] == '*'))
                {
                    position = CommentEnd(json, position);
                }
                else
                {
                    break;
                }
            }

            return position;
        }

        private static int CommentEnd(string json, int position)
        {
            if (json[position + 1] == '/')
            {
                var line = json.IndexOf('\n', position);
                return line < 0 ? json.Length : line;
            }

            var close = json.IndexOf("*/", position + 2, StringComparison.Ordinal);
            return close < 0 ? json.Length : close + 2;
        }

        // The index just after the closing quote of the string that opens at position.
        private static int StringEnd(string json, int position)
        {
            for (var index = position + 1; index < json.Length; index++)
            {
                if (json[index] == '\\')
                {
                    index++;
                }
                else if (json[index] == '"')
                {
                    return index + 1;
                }
            }

            return json.Length;
        }

        private static string Decode(string json, int start, int end)
        {
            var builder = new StringBuilder();
            for (var index = start + 1; index < end - 1; index++)
            {
                var c = json[index];
                if (c != '\\' || index + 1 >= end - 1)
                {
                    builder.Append(c);
                    continue;
                }

                var escaped = json[++index];
                switch (escaped)
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u' when index + 4 < end:
                        builder.Append((char)Convert.ToInt32(json.Substring(index + 1, 4), 16));
                        index += 4;
                        break;
                    default: builder.Append(escaped); break;
                }
            }

            return builder.ToString();
        }

        private static string Encode(string value)
        {
            var builder = new StringBuilder(value.Length + 2).Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.Append('"').ToString();
        }
    }

#if !PSEUDO_CULTURE_MARKING_ONLY
    // Writes the resource file of each pseudo-culture from each neutral one, at {OutputDirectory}/{culture}/{PseudoName}.resx,
    // where the build's items of the culture name them.
    public sealed class WritePseudoResources : Microsoft.Build.Utilities.Task
    {
        [Microsoft.Build.Framework.Required]
        public Microsoft.Build.Framework.ITaskItem[] Sources { get; set; } = Array.Empty<Microsoft.Build.Framework.ITaskItem>();

        [Microsoft.Build.Framework.Required]
        public string OutputDirectory { get; set; } = "";

        public override bool Execute()
        {
            foreach (var source in Sources)
            {
                var neutral = XDocument.Load(source.ItemSpec, LoadOptions.PreserveWhitespace);
                foreach (var culture in PseudoCulture.Names)
                {
                    var path = Path.Combine(OutputDirectory, culture, source.GetMetadata("PseudoName") + ".resx");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var resources = PseudoCulture.Resources(neutral, culture);
                    var text = resources.Declaration + Environment.NewLine + resources.Root;
                    if (!File.Exists(path) || File.ReadAllText(path) != text)
                    {
                        File.WriteAllText(path, text, new UTF8Encoding(false));
                    }
                }
            }

            return true;
        }
    }

    // Marks the menu texts of the gathered manifests in place.
    public sealed class MarkPseudoManifestLabels : Microsoft.Build.Utilities.Task
    {
        [Microsoft.Build.Framework.Required]
        public string File { get; set; } = "";

        public override bool Execute()
        {
            var text = System.IO.File.ReadAllText(File);
            var marked = PseudoManifestLabels.Mark(text);
            if (marked != text)
            {
                System.IO.File.WriteAllText(File, marked, new UTF8Encoding(false));
            }

            return true;
        }
    }
#endif
}
