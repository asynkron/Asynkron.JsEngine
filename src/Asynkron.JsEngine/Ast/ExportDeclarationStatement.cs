#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents <c>export</c> followed by a regular declaration (<c>let</c>,
///     <c>function</c>, etc.).
/// </summary>
public sealed record ExportDeclarationStatement(SourceReference? Source, StatementNode Declaration)
    : ModuleStatement(Source);
