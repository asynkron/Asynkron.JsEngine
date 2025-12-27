namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Marker interface for objects that are surfaced to JavaScript code and are mutable.
///
/// Objects implementing this interface CANNOT be pooled because:
/// 1. JavaScript code may hold references to them beyond their intended lifecycle
/// 2. JavaScript code may mutate them, so sharing instances would cause unexpected behavior
/// 3. The same instance might be observed multiple times by JavaScript code
///
/// Examples of surfaced mutable objects:
/// - JsObject, JsArray, JsFunction - core JS objects
/// - IteratorResultObject - returned from iterator.next(), JS code can hold references
/// - JsPromise - returned from async operations, JS code holds references
///
/// Counter-examples (NOT surfaced or immutable, CAN be pooled):
/// - AsyncResumeCallback - internal callback, never exposed to JS
/// - ThenMethod - while exposed via .then property, it's a method object (function)
/// - ResolvedPromiseValue - pooled but carefully managed
/// </summary>
public interface IJsSurfacedMutable
{
}
