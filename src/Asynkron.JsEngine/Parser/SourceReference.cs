namespace Asynkron.JsEngine.Parser;

/// <summary>
///     Represents a reference to a range in the original JavaScript source code.
///     Used to track the origin of s-expressions for debugging purposes.
/// </summary>
public sealed record SourceReference
{
    /// <summary>
    ///     Creates a new source reference.
    /// </summary>
    /// <param name="source">The original source text</param>
    /// <param name="startPosition">The starting position in the source (0-based index)</param>
    /// <param name="endPosition">The ending position in the source (0-based index, exclusive)</param>
    /// <param name="startLine">The starting line number (1-based)</param>
    /// <param name="startColumn">The starting column number (1-based)</param>
    /// <param name="endLine">The ending line number (1-based)</param>
    /// <param name="endColumn">The ending column number (1-based)</param>
    public SourceReference(
        string source,
        int startPosition,
        int endPosition,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        StartPosition = startPosition;
        EndPosition = endPosition;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    /// <summary>
    ///     The original source code.
    /// </summary>
    public string Source { get; }

    /// <summary>
    ///     The starting position in the source (0-based index).
    /// </summary>
    public int StartPosition { get; }

    /// <summary>
    ///     The ending position in the source (0-based index, exclusive).
    /// </summary>
    public int EndPosition { get; }

    /// <summary>
    ///     The starting line number (1-based).
    /// </summary>
    public int StartLine { get; }

    /// <summary>
    ///     The starting column number (1-based).
    /// </summary>
    public int StartColumn { get; }

    /// <summary>
    ///     The ending line number (1-based).
    /// </summary>
    public int EndLine { get; }

    /// <summary>
    ///     The ending column number (1-based).
    /// </summary>
    public int EndColumn { get; }

    /// <summary>
    ///     Gets the source text that this reference points to.
    /// </summary>
    public string GetText()
    {
        if (StartPosition >= 0 && EndPosition <= Source.Length && StartPosition <= EndPosition)
        {
            return Source[StartPosition..EndPosition];
        }

        return string.Empty;
    }

    /// <summary>
    ///     Returns a string representation showing the location of this source reference.
    /// </summary>
    public override string ToString()
    {
        return $"[{StartLine}:{StartColumn} - {EndLine}:{EndColumn}]";
    }
}
