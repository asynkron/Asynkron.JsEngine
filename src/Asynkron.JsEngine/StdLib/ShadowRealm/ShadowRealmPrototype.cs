#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// ShadowRealm provides a mechanism to execute JavaScript code in a separate realm.
/// Per ECMAScript spec, ShadowRealm.prototype.evaluate evaluates code in an isolated realm,
/// and callable results are wrapped as WrappedFunctions that enforce the realm boundary.
/// </summary>
[JsPrototype("ShadowRealm", ToStringTag = "ShadowRealm")]
public sealed partial class ShadowRealmPrototype : JsPrototype
{
    [JsHostMethod("evaluate", Length = 1d)]
    public JsValue Evaluate(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // 1. Let O be this value.
        // 2. Perform ? ValidateShadowRealmObject(O).
        var shadowRealm = ValidateShadowRealmObject(thisValue);

        // 3. If Type(sourceText) is not String, throw a TypeError.
        var sourceText = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!sourceText.IsString)
        {
            throw ThrowTypeError("ShadowRealm.prototype.evaluate requires a string argument", realm: Realm);
        }

        var source = sourceText.AsString() ?? string.Empty;

        // 4. Let callerRealm be the current Realm Record.
        var callerRealm = shadowRealm.CallerRealmState;

        // 5. Let evalRealm be O.[[ShadowRealm]].
        var innerEngine = shadowRealm.InnerEngine;

        // 6. Perform HostEnsureCanCompileStrings (verify eval is allowed).
        // 7. Perform ? PerformShadowRealmEval(sourceText, evalRealm, callerRealm).
        JsValue result;
        try
        {
            var rawResult = innerEngine.EvaluateSync(source);
            result = ConvertResult(rawResult);
        }
        catch (ThrowSignal signal)
        {
            // Per spec: errors from the other realm are wrapped into a TypeError
            // from the caller realm.
            throw ThrowTypeError(
                FormatWrappedErrorMessage(signal.ThrownValue),
                realm: callerRealm);
        }
        catch (ParseException parseEx)
        {
            // Syntax errors from evaluation are wrapped as TypeError from caller realm
            throw ThrowSyntaxError(parseEx.Message, realm: callerRealm);
        }

        // 8. Return ? GetWrappedValue(callerRealm, result).
        return GetWrappedValue(callerRealm, result, shadowRealm);
    }

    [JsHostMethod("importValue", Length = 2d)]
    public JsValue ImportValue(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // 1. Let O be this value.
        // 2. Perform ? ValidateShadowRealmObject(O).
        var shadowRealm = ValidateShadowRealmObject(thisValue);

        // 3. Let specifierString be ? ToString(specifier).
        var specifier = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (specifier.IsUndefined)
        {
            throw ThrowTypeError("ShadowRealm.prototype.importValue requires a specifier argument", realm: Realm);
        }

        var specifierString = specifier.ToJsString();

        // 4. If Type(exportName) is not String, throw a TypeError.
        var exportName = args.Count > 1 ? args[1] : JsValue.Undefined;
        if (!exportName.IsString)
        {
            throw ThrowTypeError("ShadowRealm.prototype.importValue requires a string exportName", realm: Realm);
        }

        var exportNameString = exportName.AsString() ?? string.Empty;

        // 5. Let callerRealm be the current Realm Record.
        var callerRealm = shadowRealm.CallerRealmState;
        var innerEngine = shadowRealm.InnerEngine;

        // 6-10. Create a Promise that resolves with the imported value.
        // For synchronous module evaluation:
        try
        {
            // Try to evaluate as a module in the shadow realm
            var moduleSource = $"import {{ {exportNameString} }} from '{specifierString}'; {exportNameString};";
            var rawResult = innerEngine.EvaluateSync(moduleSource);
            var result = ConvertResult(rawResult);

            var wrappedValue = GetWrappedValue(callerRealm, result, shadowRealm);

            // Return a resolved Promise with the wrapped value
            return CreateResolvedPromise(callerRealm, wrappedValue);
        }
        catch (ThrowSignal signal)
        {
            // Return a rejected Promise with a TypeError from the caller realm
            var error = CreateTypeError(FormatWrappedErrorMessage(signal.ThrownValue), realm: callerRealm);
            return CreateRejectedPromise(callerRealm, error);
        }
        catch (Exception ex)
        {
            var error = CreateTypeError(ex.Message, realm: callerRealm);
            return CreateRejectedPromise(callerRealm, error);
        }
    }

    /// <summary>
    /// Converts a raw object? result from EvaluateSync to a JsValue.
    /// Handles null (JS null), JsValue, and other objects.
    /// </summary>
    private static JsValue ConvertResult(object? rawResult)
    {
        return rawResult switch
        {
            JsValue jsVal => jsVal,
            null => JsValue.Null,
            _ => JsValue.FromObjectUnsafe(rawResult)
        };
    }

    private static JsShadowRealm ValidateShadowRealmObject(JsValue thisValue)
    {
        if (thisValue.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty(ShadowRealmConstructor.ShadowRealmSlot, out var slotVal) &&
            slotVal.TryGetObject<JsShadowRealm>(out var shadowRealm))
        {
            return shadowRealm;
        }

        throw ThrowTypeError("'this' is not a ShadowRealm object");
    }

    /// <summary>
    /// Per spec: GetWrappedValue(callerRealm, value)
    /// - Primitives pass through directly
    /// - Callable objects are wrapped as WrappedFunctions
    /// - Non-callable objects throw TypeError
    /// </summary>
    private static JsValue GetWrappedValue(RealmState callerRealm, JsValue value, JsShadowRealm shadowRealm)
    {
        // Primitive values pass through directly
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber ||
            value.IsString || value.IsBigInt || value.IsSymbol)
        {
            return value;
        }

        // Callable objects get wrapped
        if (value.TryGetObject<IJsCallable>(out var callable))
        {
            return CreateWrappedFunction(callerRealm, callable, shadowRealm);
        }

        // Non-callable objects throw TypeError
        throw ThrowTypeError(
            "ShadowRealm evaluate result must be a primitive value or a callable object",
            realm: callerRealm);
    }

    /// <summary>
    /// Creates a WrappedFunction that wraps a callable from the shadow realm.
    /// The wrapped function:
    /// - Has its prototype set to callerRealm's Function.prototype
    /// - Wraps arguments from caller to inner realm and results from inner to caller realm
    /// - Converts thrown errors to TypeErrors from the caller realm
    /// </summary>
    private static JsValue CreateWrappedFunction(RealmState callerRealm, IJsCallable target, JsShadowRealm shadowRealm)
    {
        // Get length and name from target
        var targetLength = 0d;
        var targetName = string.Empty;

        if (target is IJsPropertyAccessor targetAccessor)
        {
            // Per spec: let targetHasLength = ? HasOwnProperty(Target, "length")
            // If targetHasLength, let targetLen = ? Get(Target, "length")
            try
            {
                if (targetAccessor.TryGetProperty("length", out var lengthVal) && lengthVal.IsNumber)
                {
                    var len = lengthVal.NumberValue;
                    targetLength = double.IsNaN(len) || double.IsNegativeInfinity(len) || len < 0
                        ? 0d
                        : Math.Floor(Math.Min(len, int.MaxValue));
                }
            }
            catch (ThrowSignal)
            {
                // Per spec: if getting length throws, wrap into TypeError from caller realm
                throw ThrowTypeError("Getting 'length' from wrapped function target threw", realm: callerRealm);
            }

            try
            {
                if (targetAccessor.TryGetProperty("name", out var nameVal) && nameVal.IsString)
                {
                    targetName = nameVal.AsString() ?? string.Empty;
                }
            }
            catch (ThrowSignal)
            {
                // Per spec: if getting name throws, wrap into TypeError from caller realm
                throw ThrowTypeError("Getting 'name' from wrapped function target threw", realm: callerRealm);
            }
        }

        var wrappedFn = new HostFunction((thisVal, args) =>
        {
            // Wrap arguments: primitives pass through, callables get wrapped, objects throw
            var wrappedArgs = new JsValue[args.Count];
            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                if (arg.IsUndefined || arg.IsNull || arg.IsBoolean || arg.IsNumber ||
                    arg.IsString || arg.IsBigInt || arg.IsSymbol)
                {
                    wrappedArgs[i] = arg;
                }
                else if (arg.TryGetObject<IJsCallable>(out _))
                {
                    // Callable arguments need to be wrapped into the inner realm
                    // For simplicity, pass them through - the inner realm can call them
                    wrappedArgs[i] = arg;
                }
                else
                {
                    throw ThrowTypeError(
                        "Arguments to a wrapped function must be primitive values or callable objects",
                        realm: callerRealm);
                }
            }

            // Call the target function in the inner realm
            JsValue result;
            try
            {
                result = target.Invoke(wrappedArgs, JsValue.Undefined);
            }
            catch (ThrowSignal signal)
            {
                throw ThrowTypeError(
                    FormatWrappedErrorMessage(signal.ThrownValue),
                    realm: callerRealm);
            }

            // Wrap the result
            return GetWrappedValue(callerRealm, result, shadowRealm);
        })
        {
            RealmState = callerRealm
        };

        // Set prototype to caller realm's Function.prototype
        if (callerRealm.FunctionPrototype is { } funcProto)
        {
            wrappedFn.Properties.SetPrototype(funcProto);
        }

        // Set length and name as non-writable, non-enumerable, configurable
        wrappedFn.DefineProperty("length", new PropertyDescriptor
        {
            Value = targetLength,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        if (!string.IsNullOrEmpty(targetName))
        {
            wrappedFn.DefineProperty("name", new PropertyDescriptor
            {
                Value = targetName,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });
        }

        return JsValue.FromObjectUnsafe(wrappedFn);
    }

    private static string FormatWrappedErrorMessage(JsValue thrownValue)
    {
        if (thrownValue.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            if (accessor.TryGetProperty("message", out var msgVal) && msgVal.IsString)
            {
                var errorName = "Error";
                if (accessor.TryGetProperty("name", out var nameVal) && nameVal.IsString)
                {
                    errorName = nameVal.AsString() ?? "Error";
                }

                return $"{errorName}: {msgVal.AsString()}";
            }
        }

        if (thrownValue.IsString)
        {
            return thrownValue.AsString() ?? string.Empty;
        }

        return thrownValue.ToJsString() ?? "Error in ShadowRealm";
    }

    private static JsValue CreateResolvedPromise(RealmState realm, JsValue value)
    {
        if (realm.PromiseConstructor is IJsCallable promiseCtor &&
            promiseCtor is IJsPropertyAccessor promiseAccessor &&
            promiseAccessor.TryGetProperty("resolve", out var resolveVal) &&
            resolveVal.TryGetObject<IJsCallable>(out var resolve))
        {
            return resolve.Invoke(new SingleValueArgs(value), JsValue.FromObjectUnsafe(promiseCtor));
        }

        // Fallback: just return the value if no Promise constructor
        return value;
    }

    private static JsValue CreateRejectedPromise(RealmState realm, JsValue error)
    {
        if (realm.PromiseConstructor is IJsCallable promiseCtor &&
            promiseCtor is IJsPropertyAccessor promiseAccessor &&
            promiseAccessor.TryGetProperty("reject", out var rejectVal) &&
            rejectVal.TryGetObject<IJsCallable>(out var reject))
        {
            return reject.Invoke(new SingleValueArgs(error), JsValue.FromObjectUnsafe(promiseCtor));
        }

        // Fallback: throw
        throw new ThrowSignal(error);
    }
}
