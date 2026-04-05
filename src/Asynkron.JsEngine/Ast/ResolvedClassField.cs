#region

using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

internal readonly record struct ResolvedClassField(
    ClassField Field,
    ExpressionProgram? InitializerProgram);
