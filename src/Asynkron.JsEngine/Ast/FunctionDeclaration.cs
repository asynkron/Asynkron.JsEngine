#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a function declaration.
/// </summary>
public sealed record FunctionDeclaration(SourceReference? Source, Symbol Name, FunctionExpression Function)
    : StatementNode(Source);
