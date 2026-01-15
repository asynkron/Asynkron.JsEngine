#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncIterator prototype provides the base for async iterators.
/// Per ES spec, %AsyncIteratorPrototype%[@@asyncIterator] is a function that returns 'this'.
/// </summary>
[JsPrototype("AsyncIterator", ToStringTag = "AsyncIterator")]
public sealed partial class AsyncIteratorPrototype : JsPrototype
{
    /// <summary>
    /// %AsyncIteratorPrototype%[@@asyncIterator] returns 'this'.
    /// Per ECMAScript spec, this enables async iterators to be used with for-await-of.
    /// </summary>
    [JsSymbolMethod("asyncIterator", Length = 0d)]
    public static JsValue SelfAsyncIterator(JsValue thisValue) => thisValue;
}
