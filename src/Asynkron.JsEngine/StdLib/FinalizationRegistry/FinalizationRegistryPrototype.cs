#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("FinalizationRegistry", ToStringTag = "FinalizationRegistry")]
public sealed partial class FinalizationRegistryPrototype : JsPrototype
{
    [JsHostMethod("register", Length = 2d)]
    public JsValue Register(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // ES2024 26.2.3.2 FinalizationRegistry.prototype.register(target, heldValue [, unregisterToken])
        // 1. Let finalizationRegistry be the this value.
        // 2. Perform ? RequireInternalSlot(finalizationRegistry, [[Cells]]).
        if (!thisValue.TryGetObject<JsObject>(out var obj) || !obj.TryGetProperty("__cells__", out _))
        {
            if (!thisValue.TryGetObject<IJsPropertyAccessor>(out _))
            {
                throw ThrowTypeError("FinalizationRegistry.prototype.register called on incompatible receiver",
                    realm: Realm);
            }

            throw ThrowTypeError(
                "FinalizationRegistry.prototype.register called on an object without internal [[Cells]]",
                realm: Realm);
        }

        // 3. If target is not an Object and target is not a non-registered Symbol, throw a TypeError.
        var target = args.GetArgument(0);
        if (!CanBeHeldWeakly(target))
        {
            throw ThrowTypeError("FinalizationRegistry.prototype.register: target must be an object or a symbol",
                realm: Realm);
        }

        // 4. If SameValue(target, heldValue) is true, throw a TypeError exception.
        var heldValue = args.GetArgument(1);
        if (JsOps.SameValue(target, heldValue))
        {
            throw ThrowTypeError(
                "FinalizationRegistry.prototype.register: target and heldValue must not be the same",
                realm: Realm);
        }

        // 5. If unregisterToken is not an Object and not undefined and not a non-registered Symbol, throw TypeError.
        var unregisterToken = args.GetArgument(2);
        if (!unregisterToken.IsUndefined && !CanBeHeldWeakly(unregisterToken))
        {
            throw ThrowTypeError(
                "FinalizationRegistry.prototype.register: unregisterToken must be an object, a symbol, or undefined",
                realm: Realm);
        }

        // 6. Let cell be the Record { [[WeakRefTarget]]: target, [[HeldValue]]: heldValue, [[UnregisterToken]]: unregisterToken }.
        var cell = new JsObject();
        cell.SetProperty("__target__", target);
        cell.SetProperty("__heldValue__", heldValue);
        cell.SetProperty("__unregisterToken__", unregisterToken.IsUndefined ? JsValue.Undefined : unregisterToken);

        // 7. Append cell to finalizationRegistry.[[Cells]].
        if (obj.TryGetProperty("__cells__", out var cellsVal) && cellsVal.TryGetObject<JsArray>(out var cells))
        {
            cells.Push(JsValue.FromJsObject(cell));
        }

        // 8. Return undefined.
        return JsValue.Undefined;
    }

    [JsHostMethod("unregister", Length = 1d)]
    public JsValue Unregister(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // ES2024 26.2.3.3 FinalizationRegistry.prototype.unregister(unregisterToken)
        // 1. Let finalizationRegistry be the this value.
        // 2. Perform ? RequireInternalSlot(finalizationRegistry, [[Cells]]).
        if (!thisValue.TryGetObject<JsObject>(out var obj) || !obj.TryGetProperty("__cells__", out _))
        {
            if (!thisValue.TryGetObject<IJsPropertyAccessor>(out _))
            {
                throw ThrowTypeError("FinalizationRegistry.prototype.unregister called on incompatible receiver",
                    realm: Realm);
            }

            throw ThrowTypeError(
                "FinalizationRegistry.prototype.unregister called on an object without internal [[Cells]]",
                realm: Realm);
        }

        // 3. If unregisterToken cannot be held weakly, throw a TypeError.
        var unregisterToken = args.GetArgument(0);
        if (!CanBeHeldWeakly(unregisterToken))
        {
            throw ThrowTypeError(
                "FinalizationRegistry.prototype.unregister: unregisterToken must be an object or a symbol",
                realm: Realm);
        }

        // 4. Let removed be false.
        var removed = false;

        // 5. For each Record cell of finalizationRegistry.[[Cells]], do
        if (obj.TryGetProperty("__cells__", out var cellsVal) && cellsVal.TryGetObject<JsArray>(out var cells))
        {
            // Build a new array without the matching cells
            var newCells = new JsArray(Realm);
            for (var i = 0; i < cells.Items.Count; i++)
            {
                if (!cells.Items[i].TryGetObject<JsObject>(out var cell))
                {
                    newCells.Push(cells.Items[i]);
                    continue;
                }

                // a. If cell.[[UnregisterToken]] is not empty and SameValue(cell.[[UnregisterToken]], unregisterToken),
                if (cell.TryGetProperty("__unregisterToken__", out var tokenVal) && !tokenVal.IsUndefined
                    && JsOps.SameValue(tokenVal, unregisterToken))
                {
                    // i. Remove cell from finalizationRegistry.[[Cells]].
                    // ii. Set removed to true.
                    removed = true;
                }
                else
                {
                    newCells.Push(cells.Items[i]);
                }
            }

            // Replace the cells array
            obj.SetProperty("__cells__", JsValue.FromJsArray(newCells));
        }

        // 6. Return removed.
        return new JsValue(removed);
    }

    /// <summary>
    /// Determines if a value can be held weakly per ECMAScript spec.
    /// Objects and non-registered symbols can be held weakly.
    /// Registered symbols (created via Symbol.for()) cannot be held weakly.
    /// </summary>
    private static bool CanBeHeldWeakly(JsValue value)
    {
        if (value.IsObject)
        {
            return true;
        }

        // Symbols can be held weakly unless they are registered (via Symbol.for).
        if (value.TryUnwrap<JsSymbol>(out var sym))
        {
            // If Symbol.keyFor(sym) is not undefined, the symbol is registered and cannot be held weakly
            return JsSymbol.KeyFor(sym) is null;
        }

        return false;
    }
}
