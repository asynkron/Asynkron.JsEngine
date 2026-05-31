using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

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

        return DisposableStackHelper.Use(stack, args, preferAsync: false, Realm);
    }

    /* FLAKY */
    [JsHostMethod("adopt", Length = 2d)]
    public JsValue Adopt(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        return DisposableStackHelper.Adopt(stack, args, Realm, "DisposableStack.prototype.adopt requires a callable disposer");
    }

    /* FLAKY */
    [JsHostMethod("defer", Length = 1d)]
    public JsValue Defer(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        return DisposableStackHelper.Defer(stack, args, Realm, "DisposableStack.prototype.defer requires a callable disposer");
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

    [JsHostMethod("dispose", Length = 0d)]
    public JsValue Dispose(JsValue thisValue)
    {
        var stack = RequireStack(thisValue);
        if (stack.IsDisposed)
        {
            return JsValue.Undefined;
        }

        stack.MarkDisposed();
        ThrowSignal? currentError = null;
        while (stack.TryPopRecord(out var record))
        {
            try
            {
                record.Invoke();
            }
            catch (ThrowSignal disposeError)
            {
                if (currentError is null)
                {
                    currentError = disposeError;
                }
                else
                {
                    currentError = JsEnvironment.CreateSuppressedError(disposeError, currentError, Realm);
                }
            }
        }

        if (currentError is not null)
        {
            throw currentError;
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
        if (!thisValue.TryGetObject<JsDisposableStack>(out var stack))
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

}
