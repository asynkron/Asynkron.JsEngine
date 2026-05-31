using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.PromiseHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("AsyncDisposableStack", ToStringTag = "AsyncDisposableStack")]
[JsSymbolAlias("asyncDispose", "disposeAsync")]
public sealed partial class AsyncDisposableStackPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("use", Length = 1d)]
    public JsValue Use(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        return DisposableStackHelper.Use(stack, args, preferAsync: true, Realm);
    }

    /* FLAKY */
    [JsHostMethod("adopt", Length = 2d)]
    public JsValue Adopt(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        return DisposableStackHelper.Adopt(stack, args, Realm, "AsyncDisposableStack.prototype.adopt requires a callable disposer");
    }

    /* FLAKY */
    [JsHostMethod("defer", Length = 1d)]
    public JsValue Defer(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        return DisposableStackHelper.Defer(stack, args, Realm, "AsyncDisposableStack.prototype.defer requires a callable disposer");
    }

    /* FLAKY */
    [JsHostMethod("move", Length = 0d)]
    public JsValue Move(JsValue thisValue)
    {
        var stack = RequireStack(thisValue);
        EnsureNotDisposed(stack);

        var result = new JsAsyncDisposableStack();
        if (Realm.AsyncDisposableStackPrototype is not null)
        {
            result.SetPrototype(Realm.AsyncDisposableStackPrototype);
        }

        stack.TransferRecordsTo(result);
        stack.MarkDisposed();
        return result.AsJsValue;
    }

    /* FLAKY */
    [JsHostMethod("disposeAsync", Length = 0d)]
    public JsValue DisposeAsync(JsValue thisValue)
    {
        var promise = CreatePromise(Realm);

        if (!thisValue.TryGetObject<JsAsyncDisposableStack>(out var stack))
        {
            promise.Reject(CreateTypeError("AsyncDisposableStack.prototype.disposeAsync called on incompatible receiver",
                realm: Realm));
            return new JsValue(promise.JsObject);
        }

        if (stack.IsDisposed)
        {
            promise.Resolve(JsValue.Undefined);
            return new JsValue(promise.JsObject);
        }

        stack.MarkDisposed();

        try
        {
            while (stack.TryPopRecord(out var record))
            {
                record.Invoke();
            }

            promise.Resolve(JsValue.Undefined);
        }
        catch (ThrowSignal signal)
        {
            promise.Reject(signal.ThrownValue);
        }
        catch (Exception ex)
        {
            promise.Reject(CreateTypeError(ex.Message, realm: Realm));
        }

        return new JsValue(promise.JsObject);
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

        Realm.AsyncDisposableStackPrototype ??= Prototype as JsObject;
        EnsureDisposedAccessorMetadata();
    }

    private JsAsyncDisposableStack RequireStack(JsValue thisValue)
    {
        if (!thisValue.TryGetObject<JsAsyncDisposableStack>(out var stack))
        {
            throw ThrowTypeError("AsyncDisposableStack method called on incompatible receiver", realm: Realm);
        }

        return stack;
    }

    private void EnsureNotDisposed(JsDisposableStackBase stack)
    {
        if (stack.IsDisposed)
        {
            throw ThrowReferenceError("AsyncDisposableStack has already been disposed", realm: Realm);
        }
    }

}
