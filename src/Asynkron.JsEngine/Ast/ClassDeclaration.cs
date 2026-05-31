using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a class declaration with its fully typed definition.
/// </summary>
public sealed record ClassDeclaration(SourceReference? Source, Symbol Name, ClassDefinition Definition)
    : StatementNode(Source);
