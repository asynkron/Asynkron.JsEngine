using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine;

/// <summary>
///     Signal indicating a continue statement was encountered.
/// </summary>
internal sealed record ContinueCompletionSignal(Symbol? Label = null) : ICompletionSignal;
