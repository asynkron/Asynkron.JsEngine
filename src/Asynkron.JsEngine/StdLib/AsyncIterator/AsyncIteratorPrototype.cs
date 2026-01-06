#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncIterator prototype provides the base for async iterators
/// </summary>
[JsPrototype("AsyncIterator", ToStringTag = "AsyncIterator")]
public sealed partial class AsyncIteratorPrototype : JsPrototype
{
    // Note: AsyncIterator has Symbol methods, not regular methods
    // The main iteration is done via Symbol.asyncIterator
}
