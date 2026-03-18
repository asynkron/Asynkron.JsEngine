#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// Generator prototype for generator functions.
/// Per ES spec (sec-properties-of-generator-prototype), the methods on this prototype
/// delegate to the generator instance's own next/return/throw properties.
/// </summary>
[JsPrototype("Generator", ToStringTag = "Generator")]
public sealed partial class GeneratorPrototype : JsPrototype
{
    [JsHostMethod("next", Length = 1d)]
    public JsValue Next(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Per spec: Generator.prototype.next requires |this| to be an Object
        if (!thisValue.IsObject)
        {
            throw StandardLibrary.ThrowTypeError(
                "Generator.prototype.next requires that |this| be an Object", null, Realm);
        }

        // Delegate to the instance's own next() method
        if (!thisValue.TryGetObject<JsObject>(out var jsObject) ||
            !jsObject.ContainsKey("next") ||
            !jsObject.TryGetProperty("next", out var nextProp) ||
            !nextProp.TryGetCallable(out var nextCallable))
        {
            throw StandardLibrary.ThrowTypeError(
                "Method Generator.prototype.next called on incompatible receiver", null, Realm);
        }

        return nextCallable.Invoke(args, thisValue);
    }

    [JsHostMethod("return", Length = 1d)]
    public JsValue Return(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Per spec: Generator.prototype.return requires |this| to be an Object
        if (!thisValue.IsObject)
        {
            throw StandardLibrary.ThrowTypeError(
                "Generator.prototype.return requires that |this| be an Object", null, Realm);
        }

        // Delegate to the instance's own return() method
        if (!thisValue.TryGetObject<JsObject>(out var jsObject) ||
            !jsObject.ContainsKey("return") ||
            !jsObject.TryGetProperty("return", out var returnProp) ||
            !returnProp.TryGetCallable(out var returnCallable))
        {
            throw StandardLibrary.ThrowTypeError(
                "Method Generator.prototype.return called on incompatible receiver", null, Realm);
        }

        return returnCallable.Invoke(args, thisValue);
    }

    [JsHostMethod("throw", Length = 1d)]
    public JsValue Throw(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Per spec: Generator.prototype.throw requires |this| to be an Object
        if (!thisValue.IsObject)
        {
            throw StandardLibrary.ThrowTypeError(
                "Generator.prototype.throw requires that |this| be an Object", null, Realm);
        }

        // Delegate to the instance's own throw() method
        if (!thisValue.TryGetObject<JsObject>(out var jsObject) ||
            !jsObject.ContainsKey("throw") ||
            !jsObject.TryGetProperty("throw", out var throwProp) ||
            !throwProp.TryGetCallable(out var throwCallable))
        {
            throw StandardLibrary.ThrowTypeError(
                "Method Generator.prototype.throw called on incompatible receiver", null, Realm);
        }

        return throwCallable.Invoke(args, thisValue);
    }
}
