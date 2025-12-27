#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a function declaration in the generator.
///     Function declarations are hoisted, so this instruction is a no-op at runtime
///     that simply advances to the next instruction.
/// </summary>
internal sealed record FunctionDeclarationInstruction(int Next) : ExecutionInstruction(Next);
