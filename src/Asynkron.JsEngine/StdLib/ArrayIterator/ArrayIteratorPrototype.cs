#region

using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// ArrayIterator prototype for array iterators
/// </summary>
[JsPrototype("Array Iterator", ToStringTag = "Array Iterator")]
public sealed partial class ArrayIteratorPrototype : JsPrototype
{
    protected override void ConfigurePrototype() =>
        ConfigureAsIteratorPrototype(p => Realm.ArrayIteratorPrototype ??= p);
}
