namespace Asynkron.JsEngine.Ast;

internal readonly record struct SwitchFunctionBinding(Symbol Name, FunctionExpression Function, bool InitializeNow);
