#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an empty statement (";").
/// </summary>
public sealed record EmptyStatement(SourceReference? Source) : StatementNode(Source);
