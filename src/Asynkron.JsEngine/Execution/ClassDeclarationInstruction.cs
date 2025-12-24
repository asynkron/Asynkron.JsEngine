#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Evaluates a class declaration and binds the class constructor to the class name.
///     This instruction is used for class declarations that don't contain yields in
///     computed property names or extends clause.
/// </summary>
internal sealed record ClassDeclarationInstruction(
    int Next,
    ClassDeclaration Declaration) : GeneratorInstruction(Next);
