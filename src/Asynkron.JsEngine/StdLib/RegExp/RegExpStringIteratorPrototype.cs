#region

using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// %RegExpStringIteratorPrototype% — the prototype for RegExp string iterators
/// created by RegExp.prototype[@@matchAll].
/// </summary>
[JsPrototype("RegExp String Iterator", ToStringTag = "RegExp String Iterator")]
public sealed partial class RegExpStringIteratorPrototype : JsPrototype
{
    [JsHostMethod("next", Length = 0d)]
    private JsValue Next(JsValue thisValue)
    {
        if (!thisValue.TryGetObject<JsRegExpStringIterator>(out var iterator))
        {
            throw ThrowTypeError(
                "%RegExpStringIteratorPrototype%.next requires that 'this' be a RegExp String Iterator instance",
                realm: Realm);
        }

        return iterator.Next();
    }

    protected override void ConfigurePrototype() =>
        ConfigureAsIteratorPrototype(p => Realm.RegExpStringIteratorPrototype ??= p);
}
