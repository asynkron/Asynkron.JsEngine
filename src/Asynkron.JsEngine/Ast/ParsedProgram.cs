namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the parsed program that the runtime executes. The primary data is
///     the typed AST that flows through the evaluator.
/// </summary>
public sealed class ParsedProgram
{
    public ParsedProgram(ProgramNode typed, bool hasTopLevelAwait = false)
    {
        Typed = typed ?? throw new ArgumentNullException(nameof(typed));
        HasTopLevelAwait = hasTopLevelAwait;
    }

    public ProgramNode Typed { get; }

    public bool HasTopLevelAwait { get; }

    internal static ParsedProgram WithTyped(ProgramNode typed, bool hasTopLevelAwait = false)
    {
        return new ParsedProgram(typed, hasTopLevelAwait);
    }
}
