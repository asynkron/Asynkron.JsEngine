#region

using System.Text.RegularExpressions;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Cached regex compilation result for regex literals.
///     Stores the normalized pattern and compiled .NET Regex to avoid
///     repeated pattern normalization and regex compilation.
/// </summary>
internal sealed class RegexCache
{
    public RegexCache(string pattern, string flags)
    {
        Pattern = pattern;
        Flags = flags;

        // Validate flags and determine options
        JsRegExp.ValidateFlags(flags);

        var hasUnicodeFlag = flags.Contains('u', StringComparison.Ordinal) ||
                             flags.Contains('v', StringComparison.Ordinal);
        var ignoreCase = flags.Contains('i', StringComparison.Ordinal);
        var multiline = flags.Contains('m', StringComparison.Ordinal);

        // Normalize the pattern (expensive operation - done once)
        NormalizedPattern = JsRegExp.NormalizePattern(pattern, hasUnicodeFlag, ignoreCase);

        // Build RegexOptions
        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }
        if (multiline)
        {
            options |= RegexOptions.Multiline;
        }
        RegexOptions = options;

        // Compile the regex (expensive operation - done once)
        CompiledRegex = new Regex(NormalizedPattern, options);
    }

    public string Pattern { get; }
    public string Flags { get; }
    public string NormalizedPattern { get; }
    public RegexOptions RegexOptions { get; }
    public Regex CompiledRegex { get; }
}
