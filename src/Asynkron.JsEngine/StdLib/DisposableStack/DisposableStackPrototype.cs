#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("DisposableStack", ToStringTag = "DisposableStack")]
[JsSymbolAlias("dispose", "dispose")]
public sealed partial class DisposableStackPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("use", Length = 1d)]
    public JsValue Use(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        var value = args.GetArgument(0);
        var disposeMethod = DisposableStackHelper.GetDisposeMethod(value, preferAsync: false, Realm);
        if (disposeMethod is null)
        {
            return value;
        }

        stack.AddRecord(disposeMethod, value, []);
        return value;
    }

    /* FLAKY */
    [JsHostMethod("adopt", Length = 2d)]
    public JsValue Adopt(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        var value = args.GetArgument(0);
        var onDispose = args.GetArgument(1);
        if (!onDispose.TryGetObject<IJsCallable>(out var callable) || callable is null)
        {
            throw ThrowTypeError("DisposableStack.prototype.adopt requires a callable disposer", realm: Realm);
        }

        stack.AddRecord(callable, JsValue.Undefined, [value]);
        return value;
    }

    /* FLAKY */
    [JsHostMethod("defer", Length = 1d)]
    public JsValue Defer(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        var onDispose = args.GetArgument(0);
        if (!onDispose.TryGetObject<IJsCallable>(out var callable) || callable is null)
        {
            throw ThrowTypeError("DisposableStack.prototype.defer requires a callable disposer", realm: Realm);
        }

        stack.AddRecord(callable, JsValue.Undefined, []);
        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostMethod("move", Length = 0d)]
    public JsValue Move(JsValue thisValue)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        var result = new JsDisposableStack();
        if (Realm.DisposableStackPrototype is not null)
        {
            result.SetPrototype(Realm.DisposableStackPrototype);
        }

        stack.TransferRecordsTo(result);
        stack.MarkDisposed();
        return result.AsJsValue;
    }

    /* FLAKY */
    [JsHostMethod("dispose", Length = 0d)]
    public JsValue Dispose(JsValue thisValue)
    {
        var stack = RequireStack(thisValue);
        if (stack.IsDisposed)
        {
            return JsValue.Undefined;
        }

        stack.MarkDisposed();
        while (stack.TryPopRecord(out var record))
        {
            record.Invoke();
        }

        return JsValue.Undefined;
    }

    /* FLAKY */
    [JsHostGetter("disposed")]
    public JsValue Disposed(JsValue thisValue)
    {
        var stack = RequireStack(thisValue);
        return new JsValue(stack.IsDisposed);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.DisposableStackPrototype ??= Prototype as JsObject;
        EnsureDisposedAccessorMetadata();
    }

    private JsDisposableStack RequireStack(JsValue thisValue)
    {
        if (!thisValue.TryGetObject<JsDisposableStack>(out var stack) || stack is null)
        {
            throw ThrowTypeError("DisposableStack method called on incompatible receiver", realm: Realm);
        }

        return stack;
    }

    private void EnsureNotDisposed(JsDisposableStackBase stack)
    {
        if (stack.IsDisposed)
        {
            throw ThrowReferenceError("DisposableStack has already been disposed", realm: Realm);
        }
    }

    private void EnsureDisposedAccessorMetadata()
    {
        if (Prototype.GetOwnPropertyDescriptor("disposed") is not { Get: { } getter })
        {
            return;
        }

        if (getter is not IJsObjectLike getterObj)
        {
            return;
        }

        EnsureFunctionMetadata(getterObj, "length", 0d);
        EnsureFunctionMetadata(getterObj, "name", "get disposed");
    }
}
