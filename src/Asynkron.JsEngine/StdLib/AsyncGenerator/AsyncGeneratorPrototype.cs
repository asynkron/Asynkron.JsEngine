#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.PromiseHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncGenerator prototype for async generator functions
/// </summary>
[JsPrototype("AsyncGenerator", ToStringTag = "AsyncGenerator")]
public sealed partial class AsyncGeneratorPrototype : JsPrototype
{
    [JsHostMethod("next", Length = 1d)]
    public JsValue Next(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Per spec: if this is not an object, return a rejected promise with TypeError
        if (!thisValue.IsObject)
        {
            return CreateRejectedPromiseWithTypeError("AsyncGenerator.prototype.next requires that |this| be an Object");
        }

        // Check if this is a valid async generator instance.
        // Valid async generator instances:
        // 1. Have their own "next" property (set by CreateGeneratorIteratorObject)
        // 2. Have Symbol.asyncIterator property (distinguishes async from sync generators)
        if (!thisValue.TryGetObject<JsObject>(out var jsObject) ||
            !jsObject.ContainsKey("next") ||
            !jsObject.ContainsKey(SymbolKeys.AsyncIterator) ||
            !jsObject.TryGetProperty("next", out var nextProp) ||
            !nextProp.TryGetCallable(out var nextCallable))
        {
            return CreateRejectedPromiseWithTypeError("Method AsyncGenerator.prototype.next called on incompatible receiver");
        }

        // The instance has its own next() implementation - delegate to it
        return nextCallable.Invoke(args, thisValue);
    }

    [JsHostMethod("return", Length = 1d)]
    public JsValue Return(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Per spec: if this is not an object, return a rejected promise with TypeError
        if (!thisValue.IsObject)
        {
            return CreateRejectedPromiseWithTypeError("AsyncGenerator.prototype.return requires that |this| be an Object");
        }

        // Check if this is a valid async generator instance with its own "return" property
        // and Symbol.asyncIterator (to distinguish from sync generators).
        if (!thisValue.TryGetObject<JsObject>(out var jsObject) ||
            !jsObject.ContainsKey("return") ||
            !jsObject.ContainsKey(SymbolKeys.AsyncIterator) ||
            !jsObject.TryGetProperty("return", out var returnProp) ||
            !returnProp.TryGetCallable(out var returnCallable))
        {
            return CreateRejectedPromiseWithTypeError("Method AsyncGenerator.prototype.return called on incompatible receiver");
        }

        // The instance has its own return() implementation - delegate to it
        return returnCallable.Invoke(args, thisValue);
    }

    [JsHostMethod("throw", Length = 1d)]
    public JsValue Throw(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // Per spec: if this is not an object, return a rejected promise with TypeError
        if (!thisValue.IsObject)
        {
            return CreateRejectedPromiseWithTypeError("AsyncGenerator.prototype.throw requires that |this| be an Object");
        }

        // Check if this is a valid async generator instance with its own "throw" property
        // and Symbol.asyncIterator (to distinguish from sync generators).
        if (!thisValue.TryGetObject<JsObject>(out var jsObject) ||
            !jsObject.ContainsKey("throw") ||
            !jsObject.ContainsKey(SymbolKeys.AsyncIterator) ||
            !jsObject.TryGetProperty("throw", out var throwProp) ||
            !throwProp.TryGetCallable(out var throwCallable))
        {
            return CreateRejectedPromiseWithTypeError("Method AsyncGenerator.prototype.throw called on incompatible receiver");
        }

        // The instance has its own throw() implementation - delegate to it
        return throwCallable.Invoke(args, thisValue);
    }

    /// <summary>
    /// Creates a rejected promise with a TypeError message.
    /// </summary>
    private JsValue CreateRejectedPromiseWithTypeError(string message)
    {
        var promise = CreatePromise(Realm);
        var typeError = CreateTypeError(message, realm: Realm);
        promise.Reject(typeError);

        return (JsValue)promise.JsObject;
    }
}
