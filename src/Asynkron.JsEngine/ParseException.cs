namespace Asynkron.JsEngine;

public sealed class ParseException : Exception
{
    public int? Line { get; }
    public int? Column { get; }

    public ParseException(string message) : base(message)
    {
    }

    public ParseException(string message, int line, int column) 
        : base($"{message} at line {line}, column {column}")
    {
        Line = line;
        Column = column;
    }

    public ParseException(string message, Token token) 
        : base($"{message} at line {token.Line}, column {token.Column}")
    {
        Line = token.Line;
        Column = token.Column;
    }

    public ParseException(string message, Token token, string source)
        : base(FormatMessageWithSnippet(message, token, source))
    {
        Line = token.Line;
        Column = token.Column;
    }

    private static string FormatMessageWithSnippet(string message, Token token, string source)
    {
        var snippet = ExtractSourceSnippet(source, token.StartPosition, 20);
        
        // Check if message already contains position info
        var posPattern = $"at line {token.Line} column {token.Column}";
        var posPattern2 = $"at line {token.Line}, column {token.Column}";
        
        // Remove duplicate position info if present
        if (message.Contains(posPattern))
        {
            message = message.Replace(posPattern + ".", "").Trim();
            if (message.EndsWith(".")) message = message[..^1];
        }
        else if (message.Contains(posPattern2))
        {
            message = message.Replace(posPattern2, "").Trim();
            if (message.EndsWith(".")) message = message[..^1];
        }
        
        return $"{message} at line {token.Line}, column {token.Column}\n{snippet}";
    }

    /// <summary>
    /// Extracts a snippet of source code around the given position.
    /// </summary>
    /// <param name="source">The full source code</param>
    /// <param name="position">The error position in the source</param>
    /// <param name="contextChars">Number of characters to show before and after the position (default 20)</param>
    /// <returns>A formatted snippet showing the error context</returns>
    private static string ExtractSourceSnippet(string source, int position, int contextChars = 20)
    {
        if (string.IsNullOrEmpty(source) || position < 0 || position > source.Length)
        {
            return string.Empty;
        }

        // Calculate start and end positions for the snippet
        var startPos = Math.Max(0, position - contextChars);
        var endPos = Math.Min(source.Length, position + contextChars);

        // Extract the snippet
        var snippet = source[startPos..endPos];

        // Add ellipsis if we're not at the boundaries
        var prefix = startPos > 0 ? "..." : "";
        var suffix = endPos < source.Length ? "..." : "";

        // Calculate the position of the error marker relative to the snippet
        var errorOffset = position - startPos + prefix.Length;

        // Build the error display with a marker
        var marker = new string(' ', errorOffset) + "^";

        return $"Source context:\n{prefix}{snippet}{suffix}\n{marker}";
    }
}