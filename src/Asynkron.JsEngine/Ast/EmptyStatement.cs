using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an empty statement (";").
/// </summary>
public sealed record EmptyStatement(SourceReference? Source) : StatementNode(Source);
