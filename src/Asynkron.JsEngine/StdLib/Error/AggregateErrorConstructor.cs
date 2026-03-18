#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("AggregateError", PrototypeType = typeof(ErrorPrototype), Length = 2d, DisplayName = "AggregateError")]
public sealed partial class AggregateErrorConstructor(IJsObjectLike prototype, RealmState realm)
    : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "AggregateError";

    /// <summary>
    /// AggregateError ( errors, message[ , options ] )
    /// Per spec, args[0] is errors, args[1] is message, args[2] is options.
    /// The base ErrorConstructorBase.InitializeError treats args[0] as message and args[1] as options.
    /// We override to handle the different argument ordering and set the errors property.
    /// </summary>
    protected override void InitializeError(JsObject instance, IReadOnlyList<JsValue> args)
    {
        instance.RealmState ??= Realm;

        // ES spec: Set [[ErrorData]] internal slot
        instance.DefineProperty("_errorData",
            new PropertyDescriptor { Value = JsValue.True, Writable = false, Enumerable = false, Configurable = false });

        // args[0] = errors, args[1] = message, args[2] = options
        var errorsArg = args.Count > 0 ? args[0] : JsValue.Undefined;
        var messageArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var optionsArg = args.Count > 2 ? args[2] : JsValue.Undefined;

        // Set message if provided and not undefined
        if (!messageArg.IsUndefined)
        {
            var message = messageArg.IsNull ? "null" : JsOps.ToJsString(messageArg);
            instance.DefineProperty("message",
                new PropertyDescriptor { Value = message, Writable = true, Enumerable = false, Configurable = true });
        }

        // ES2022: InstallErrorCause - handle options.cause
        if (optionsArg.IsObject)
        {
            if (optionsArg.TryGetObject<IJsPropertyAccessor>(out var optionsAccessor) &&
                HasProperty(optionsAccessor, "cause"))
            {
                JsOps.TryGetPropertyValue(optionsArg, "cause", out var cause);
                instance.DefineProperty("cause",
                    new PropertyDescriptor { Value = cause, Writable = true, Enumerable = false, Configurable = true });
            }
        }

        // Per spec: Perform ! CreateDataPropertyOrThrow(O, "errors", CreateArrayFromList(errorsList)).
        // Step 3: Let errorsList be ? IterableToList(errors).
        var errorsList = IterableToList(errorsArg);
        instance.DefineProperty("errors",
            new PropertyDescriptor { Value = errorsList, Writable = true, Enumerable = false, Configurable = true });
    }

    private JsValue IterableToList(JsValue iterable)
    {
        var array = new JsTypes.JsArray(Realm);

        // Get the iterator
        if (!JsOps.TryGetPropertyValue(iterable, SymbolKeys.Iterator, out var iteratorMethod))
        {
            throw ThrowTypeError("errors is not iterable", realm: Realm);
        }

        if (!iteratorMethod.TryGetObject<IJsCallable>(out var iteratorCallable))
        {
            throw ThrowTypeError("errors is not iterable", realm: Realm);
        }

        var iteratorResult = iteratorCallable.Invoke(new SingleValueArgs(iterable), iterable);
        if (!iteratorResult.TryGetObject<IJsPropertyAccessor>(out var iterator))
        {
            throw ThrowTypeError("Iterator result is not an object", realm: Realm);
        }

        // Get the next method
        if (!iterator.TryGetProperty("next", out var nextMethod) ||
            !nextMethod.TryGetObject<IJsCallable>(out var nextCallable))
        {
            throw ThrowTypeError("Iterator does not have a next method", realm: Realm);
        }

        while (true)
        {
            var next = nextCallable.Invoke([], iteratorResult);
            if (!next.TryGetObject<IJsPropertyAccessor>(out var nextObj))
            {
                throw ThrowTypeError("Iterator result is not an object", realm: Realm);
            }

            if (nextObj.TryGetProperty("done", out var done) && JsOps.ToBoolean(done))
            {
                break;
            }

            nextObj.TryGetProperty("value", out var value);
            array.Push(value);
        }

        return JsValue.FromObjectUnsafe(array);
    }
}
