using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a static initialization block within a class.
/// </summary>
public sealed record ClassStaticBlock(SourceReference? Source, BlockStatement Body) : AstNode(Source);
