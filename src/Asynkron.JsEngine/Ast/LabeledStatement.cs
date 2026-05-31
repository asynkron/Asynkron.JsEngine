using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a labeled statement.
/// </summary>
public sealed record LabeledStatement(SourceReference? Source, Symbol Label, StatementNode Statement)
    : StatementNode(Source);
