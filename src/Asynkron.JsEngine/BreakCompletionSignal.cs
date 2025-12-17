using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a break statement was encountered.
/// </summary>
internal sealed record BreakCompletionSignal(Symbol? Label = null) : ICompletionSignal;
