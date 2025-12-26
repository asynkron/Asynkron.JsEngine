



namespace Asynkron.JsParser;

/// <summary>
///     Represents a class declaration with its fully typed definition.
/// </summary>
public sealed record ClassDeclaration(SourceReference? Source, Symbol Name, ClassDefinition Definition)
    : StatementNode(Source);
