namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Captures the textual order of static initialization steps (fields/blocks).
/// </summary>
public readonly record struct ClassStaticElement(ClassStaticElementKind Kind, int Index);
