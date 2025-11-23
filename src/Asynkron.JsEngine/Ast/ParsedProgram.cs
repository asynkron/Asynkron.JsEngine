namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the parsed program that the runtime executes. The primary data is
///     the typed AST that flows through the evaluator.
/// </summary>
public sealed class ParsedProgram(ProgramNode typed)
{
    public ProgramNode Typed { get; } = typed ?? throw new ArgumentNullException(nameof(typed));

    internal static ParsedProgram WithTyped(ProgramNode typed)
    {
        return new ParsedProgram(typed);
    }
}
