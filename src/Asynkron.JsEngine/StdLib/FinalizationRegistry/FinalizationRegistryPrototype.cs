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
        var state = RequireInternalSlot(thisValue, "register");

        var target = args.GetArgument(0);
        if (!CanBeHeldWeakly(target))
        {
            throw ThrowTypeError("FinalizationRegistry.prototype.register: target must be an object or a symbol",
                realm: Realm);
        }

        var heldValue = args.GetArgument(1);
        if (JsOps.SameValue(target, heldValue))
        {
            throw ThrowTypeError(
                "FinalizationRegistry.prototype.register: target and heldValue must not be the same",
                realm: Realm);
        }

        var unregisterToken = args.GetArgument(2);
        if (!unregisterToken.IsUndefined && !CanBeHeldWeakly(unregisterToken))
        {
            throw ThrowTypeError(
                "FinalizationRegistry.prototype.register: unregisterToken must be an object, a symbol, or undefined",
                realm: Realm);
        }

        var cell = new JsObject();
        cell.SetProperty("__target__", target);
        cell.SetProperty("__heldValue__", heldValue);
        cell.SetProperty("__unregisterToken__", unregisterToken.IsUndefined ? JsValue.Undefined : unregisterToken);

        state.Cells.Push(JsValue.FromJsObject(cell));

        return JsValue.Undefined;
    }

    [JsHostMethod("unregister", Length = 1d)]
    public JsValue Unregister(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var state = RequireInternalSlot(thisValue, "unregister");

        var unregisterToken = args.GetArgument(0);
        if (!CanBeHeldWeakly(unregisterToken))
        {
            throw ThrowTypeError(
                "FinalizationRegistry.prototype.unregister: unregisterToken must be an object or a symbol",
                realm: Realm);
        }

        var removed = false;
        var cells = state.Cells;
        var newCells = new JsArray(Realm);
        for (var i = 0; i < cells.Items.Count; i++)
        {
            if (!cells.Items[i].TryGetObject<JsObject>(out var cell))
            {
                newCells.Push(cells.Items[i]);
                continue;
            }

            if (cell.TryGetProperty("__unregisterToken__", out var tokenVal) && !tokenVal.IsUndefined
                && JsOps.SameValue(tokenVal, unregisterToken))
            {
                removed = true;
            }
            else
            {
                newCells.Push(cells.Items[i]);
            }
        }

        state.Cells = newCells;
        return new JsValue(removed);
    }

    private FinalizationRegistryState RequireInternalSlot(JsValue thisValue, string methodName)
    {
        if (thisValue.TryGetObject<JsObject>(out var obj))
        {
            var state = FinalizationRegistryConstructor.GetState(obj);
            if (state is not null)
            {
                return state;
            }
        }

        throw ThrowTypeError(
            $"FinalizationRegistry.prototype.{methodName} called on incompatible receiver",
            realm: Realm);
    }

    private static bool CanBeHeldWeakly(JsValue value)
    {
        if (value.IsObject)
        {
            return true;
        }

        if (value.TryUnwrap<JsSymbol>(out var sym))
        {
            return JsSymbol.KeyFor(sym) is null;
        }

        return false;
    }
}
