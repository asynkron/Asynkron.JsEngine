using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for statements.
/// </summary>
public abstract record StatementNode(SourceReference? Source) : AstNode(Source);
