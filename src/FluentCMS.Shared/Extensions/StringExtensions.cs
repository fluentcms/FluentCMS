using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Shared.Extensions;
public static class StringExtensions
{
    // from PascalCase to kebab-case
    public static string FromPascalCaseToKebabCase(this string value)
    {
        return string.Concat(value.Select((x, i) => i > 0 && char.IsUpper(x) ? "-" + x : x.ToString())).ToLower();
    }
}
