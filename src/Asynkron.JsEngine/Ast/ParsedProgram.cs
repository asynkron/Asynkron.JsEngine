namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Represents the parsed program that the runtime executes. The primary data is
/// the typed AST that flows through the evaluator.
/// </summary>
public sealed class ParsedProgram
{
    public ParsedProgram(ProgramNode typed)
    {
        Typed = typed ?? throw new ArgumentNullException(nameof(typed));
    }

    public ProgramNode Typed { get; }

    internal ParsedProgram WithTyped(ProgramNode typed)
    {
        return new ParsedProgram(typed);
    }
}
