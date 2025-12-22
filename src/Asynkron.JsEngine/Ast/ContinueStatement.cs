#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a continue statement, optionally labeled.
/// </summary>
public sealed record ContinueStatement(SourceReference? Source, Symbol? Label) : StatementNode(Source);
