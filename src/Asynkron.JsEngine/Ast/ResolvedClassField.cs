#region

using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

internal readonly record struct ResolvedClassField(
    string Name,
    bool IsStatic,
    bool IsPrivate,
    string? AnonymousFunctionName,
    ExpressionProgram? InitializerProgram);
