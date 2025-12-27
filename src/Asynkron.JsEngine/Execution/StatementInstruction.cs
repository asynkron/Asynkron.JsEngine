#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Evaluates a statement node and then jumps to <see cref="ExecutionInstruction.Next" />.
/// </summary>
internal sealed record StatementInstruction(int Next, StatementNode Statement) : ExecutionInstruction(Next);
