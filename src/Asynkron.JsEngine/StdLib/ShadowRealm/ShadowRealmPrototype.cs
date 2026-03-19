#region

using Asynkron.JsEngine.Ast;
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
        // Per spec, errors thrown here use the built-in function's realm (F.[[Realm]])
        var shadowRealm = ValidateShadowRealmObject(thisValue, Realm);

        // 3. If Type(sourceText) is not String, throw a TypeError.
        var sourceText = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (!sourceText.IsString)
        {
            throw ThrowTypeError("ShadowRealm.prototype.evaluate requires a string argument", realm: Realm);
        }

        var source = sourceText.AsString() ?? string.Empty;

        // 4. Let callerRealm be the current Realm Record.
        // Per spec: for a built-in function, the current Realm Record is F.[[Realm]].
        // This is the realm of the prototype/constructor, not the realm of the calling code.
        var callerRealm = Realm;

        // 5. Let evalRealm be O.[[ShadowRealm]].
        var innerEngine = shadowRealm.InnerEngine;
        var innerRealm = innerEngine.RealmState;

        // 6. Perform HostEnsureCanCompileStrings (verify eval is allowed).
        // 7. Perform ? PerformShadowRealmEval(sourceText, evalRealm, callerRealm).
        // Per spec, PerformShadowRealmEval creates a fresh lexical environment
        // (like eval) so const/let bindings don't persist across evaluations.
        JsValue result;
        try
        {
            var program = innerEngine.ParseProgram(source);
            var rawResult = program.EvaluateProgram(
                innerEngine.GlobalEnvironment,
                innerRealm,
                executionKind: ExecutionKind.Eval);
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
            // Syntax errors from evaluation are wrapped as SyntaxError from caller realm
            throw ThrowSyntaxError(parseEx.Message, realm: callerRealm);
        }

        // 8. Return ? GetWrappedValue(callerRealm, result).
        return GetWrappedValue(callerRealm, result, innerRealm, shadowRealm);
    }

    [JsHostMethod("importValue", Length = 2d)]
    public JsValue ImportValue(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // 1. Let O be this value.
        // 2. Perform ? ValidateShadowRealmObject(O).
        var shadowRealm = ValidateShadowRealmObject(thisValue, Realm);

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
        var callerRealm = Realm;
        var innerEngine = shadowRealm.InnerEngine;

        // 6-10. Create a Promise that resolves with the imported value.
        // For synchronous module evaluation:
        try
        {
            // Try to evaluate as a module in the shadow realm
            var moduleSource = $"import {{ {exportNameString} }} from '{specifierString}'; {exportNameString};";
            var rawResult = innerEngine.EvaluateSync(moduleSource);
            var result = ConvertResult(rawResult);

            var wrappedValue = GetWrappedValue(callerRealm, result, innerEngine.RealmState, shadowRealm);

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

    private static JsShadowRealm ValidateShadowRealmObject(JsValue thisValue, RealmState realm)
    {
        if (thisValue.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty(ShadowRealmConstructor.ShadowRealmSlot, out var slotVal) &&
            slotVal.TryGetObject<JsShadowRealm>(out var shadowRealm))
        {
            return shadowRealm;
        }

        throw ThrowTypeError("'this' is not a ShadowRealm object", realm: realm);
    }

    /// <summary>
    /// Per spec: GetWrappedValue(callerRealm, value)
    /// - Primitives pass through directly
    /// - Callable objects are wrapped as WrappedFunctions
    /// - Non-callable objects throw TypeError
    /// </summary>
    /// <param name="wrapperRealm">The realm where the WrappedFunction will live</param>
    /// <param name="value">The value to wrap</param>
    /// <param name="targetRealm">The realm where the value originated</param>
    /// <param name="shadowRealm">The ShadowRealm context for recursive wrapping</param>
    private static JsValue GetWrappedValue(RealmState wrapperRealm, JsValue value,
        RealmState targetRealm, JsShadowRealm shadowRealm)
    {
        // Primitive values pass through directly
        if (value.IsUndefined || value.IsNull || value.IsBoolean || value.IsNumber ||
            value.IsString || value.IsBigInt || value.IsSymbol)
        {
            return value;
        }

        // Callable objects get wrapped — use JsOps.IsCallable to correctly handle proxies
        // (a Proxy is only callable if its target is callable, per spec)
        if (JsOps.IsCallable(value))
        {
            var callable = (IJsCallable)value.ObjectValue!;
            return CreateWrappedFunction(wrapperRealm, callable, targetRealm, shadowRealm);
        }

        // Non-callable objects throw TypeError
        throw ThrowTypeError(
            "ShadowRealm evaluate result must be a primitive value or a callable object",
            realm: wrapperRealm);
    }

    /// <summary>
    /// Creates a WrappedFunction that wraps a callable from another realm.
    /// Per spec (WrappedFunctionCreate + [[Call]]):
    /// - The WrappedFunction lives in wrapperRealm (its prototype, errors, etc.)
    /// - When called, arguments are wrapped into targetRealm (the target function's realm)
    /// - Results are wrapped back into wrapperRealm
    /// - Thrown errors become TypeErrors from wrapperRealm
    /// </summary>
    /// <param name="wrapperRealm">The realm where this WrappedFunction lives</param>
    /// <param name="target">The callable being wrapped</param>
    /// <param name="targetRealm">The realm where the target function lives</param>
    /// <param name="shadowRealm">The ShadowRealm context for recursive wrapping</param>
    private static JsValue CreateWrappedFunction(RealmState wrapperRealm, IJsCallable target,
        RealmState targetRealm, JsShadowRealm shadowRealm)
    {
        // Per spec: CopyNameAndLength(wrapped, Target)
        // Uses ? HasOwnProperty(Target, "length") and ? Get(Target, "length")
        // The ? means exceptions propagate and are wrapped to TypeError from callerRealm
        var targetLength = 0d;
        var targetName = string.Empty;

        if (target is IJsPropertyAccessor targetAccessor)
        {
            // Per spec: let targetHasLength = ? HasOwnProperty(Target, "length")
            // HasOwnProperty calls [[GetOwnProperty]] which triggers proxy getOwnPropertyDescriptor trap
            try
            {
                PropertyDescriptor? lengthDesc = null;
                if (target is IJsObjectLike objectLike)
                {
                    lengthDesc = objectLike.GetOwnPropertyDescriptor("length");
                }

                if (lengthDesc is not null)
                {
                    // Per spec: let targetLen = ? Get(Target, "length")
                    if (targetAccessor.TryGetProperty("length", out var lengthVal) && lengthVal.IsNumber)
                    {
                        var len = lengthVal.NumberValue;
                        if (double.IsPositiveInfinity(len))
                        {
                            targetLength = double.PositiveInfinity;
                        }
                        else if (double.IsNaN(len) || double.IsNegativeInfinity(len) || len < 0)
                        {
                            targetLength = 0d;
                        }
                        else
                        {
                            targetLength = Math.Floor(len);
                        }
                    }
                }
                else if (lengthDesc is null && target is not IJsObjectLike)
                {
                    // Fallback for non-IJsObjectLike targets: use TryGetProperty
                    if (targetAccessor.TryGetProperty("length", out var lengthVal) && lengthVal.IsNumber)
                    {
                        var len = lengthVal.NumberValue;
                        if (double.IsPositiveInfinity(len))
                        {
                            targetLength = double.PositiveInfinity;
                        }
                        else if (double.IsNaN(len) || double.IsNegativeInfinity(len) || len < 0)
                        {
                            targetLength = 0d;
                        }
                        else
                        {
                            targetLength = Math.Floor(len);
                        }
                    }
                }
            }
            catch (ThrowSignal)
            {
                // Per spec: if HasOwnProperty or Get throws, wrap into TypeError from caller realm
                throw ThrowTypeError("Getting 'length' from wrapped function target threw", realm: wrapperRealm);
            }

            // Per spec: let targetName = ? Get(Target, "name")
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
                throw ThrowTypeError("Getting 'name' from wrapped function target threw", realm: wrapperRealm);
            }
        }

        var wrappedFn = new HostFunction((_, args) =>
        {
            // Per spec [[Call]] step 6: For each arg, GetWrappedValue(targetRealm, arg)
            // Arguments are wrapped into the target function's realm
            var wrappedArgs = new JsValue[args.Count];
            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                if (arg.IsUndefined || arg.IsNull || arg.IsBoolean || arg.IsNumber ||
                    arg.IsString || arg.IsBigInt || arg.IsSymbol)
                {
                    wrappedArgs[i] = arg;
                }
                else if (JsOps.IsCallable(arg))
                {
                    // Callable arguments are wrapped into the target's realm
                    // The wrapped callable lives in targetRealm, and its target is in wrapperRealm
                    var callableArg = (IJsCallable)arg.ObjectValue!;
                    wrappedArgs[i] = CreateWrappedFunction(targetRealm, callableArg, wrapperRealm, shadowRealm);
                }
                else
                {
                    throw ThrowTypeError(
                        "Arguments to a wrapped function must be primitive values or callable objects",
                        realm: wrapperRealm);
                }
            }

            // Call the target function in the target realm
            JsValue result;
            try
            {
                result = target.Invoke(wrappedArgs, JsValue.Undefined);
            }
            catch (ThrowSignal)
            {
                // Per spec [[Call]] step 9: if Call threw, throw a TypeError from callerRealm
                // callerRealm = the realm of the WrappedFunction = wrapperRealm
                throw ThrowTypeError(
                    "Call to a wrapped function threw in the target realm",
                    realm: wrapperRealm);
            }

            // Per spec [[Call]] step 9: return GetWrappedValue(callerRealm, result)
            // callerRealm = the realm of the WrappedFunction = wrapperRealm
            return GetWrappedValue(wrapperRealm, result, targetRealm, shadowRealm);
        })
        {
            RealmState = wrapperRealm
        };

        // Set prototype to wrapper realm's Function.prototype
        if (wrapperRealm.FunctionPrototype is { } funcProto)
        {
            wrappedFn.Properties.SetPrototype(funcProto);
        }

        // Per spec: SetFunctionLength — non-writable, non-enumerable, configurable
        wrappedFn.DefineProperty("length", new PropertyDescriptor
        {
            Value = targetLength,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        // Per spec: SetFunctionName — always set, even if empty string
        wrappedFn.DefineProperty("name", new PropertyDescriptor
        {
            Value = targetName,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

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
