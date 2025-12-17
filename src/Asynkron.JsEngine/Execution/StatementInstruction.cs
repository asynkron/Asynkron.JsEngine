using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Evaluates a statement node and then jumps to <see cref="GeneratorInstruction.Next" />.
/// </summary>
internal sealed record StatementInstruction(int Next, StatementNode Statement) : GeneratorInstruction(Next);
