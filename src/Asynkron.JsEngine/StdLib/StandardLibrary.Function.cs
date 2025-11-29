using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateFunctionConstructor(RealmState realm, JsEngine engine)
    {
        HostFunction functionConstructor = null!;

        functionConstructor = new HostFunction((_, args) =>
        {
            var evalContext = realm.CreateContext();
            var argCount = args.Count;
            var bodyValue = argCount > 0 ? args[argCount - 1] : string.Empty;
            var parameterCount = Math.Max(argCount - 1, 0);

            var parameters = new string[parameterCount];
            for (var i = 0; i < parameterCount; i++)
            {
                var paramText = ToFunctionArgumentString(args[i], evalContext, realm);
                parameters[i] = paramText;
            }

            var bodySource = ToFunctionArgumentString(bodyValue, evalContext, realm);
            var paramList = string.Join(",", parameters);
            var hasDanglingClose = ContainsHtmlCloseCommentWithoutLineTerminator(paramList);
            if (hasDanglingClose)
            {
                throw ThrowSyntaxError("Invalid function parameter list", evalContext, realm);
            }

            // ECMAScript builds the source with line feeds around the parameter list and body,
            // so HTML-like comments (<!--/-->) are recognized using the Script goal rules.
            var functionSource = $"(function anonymous({paramList}\n) {{\n{bodySource}\n}})";

            ParsedProgram program;
            try
            {
                program = engine.ParseForExecution(functionSource);
            }
            catch (ParseException parseException)
            {
                var message = parseException.Message ?? "SyntaxError";
                throw new ThrowSignal(CreateSyntaxError(message, evalContext, realm));
            }

            return engine.ExecuteProgram(
                program,
                engine.GlobalEnvironment,
                CancellationToken.None);
        }) { RealmState = realm };

        // Function.call: when used as `fn.call(thisArg, ...args)` the
        // target function is `fn` (the `this` value). We implement this
        // directly so that binding `Function.call` or
        // `Function.prototype.call` produces helpers that behave like
        // `Function.prototype.call`.
        var callHelper = new HostFunction((thisValue, args) =>
        {
            if (thisValue is not IJsCallable target)
            {
                return Symbol.Undefined;
            }

            object? thisArg = Symbol.Undefined;
            var callArgs = Array.Empty<object?>();

            if (args.Count > 0)
            {
                thisArg = args[0];
                if (args.Count > 1)
                {
                    callArgs = args.Skip(1).ToArray();
                }
            }

            return target.Invoke(callArgs, thisArg);
        });
        callHelper.Realm = functionConstructor.Realm;
        callHelper.RealmState = functionConstructor.RealmState;

        functionConstructor.SetProperty("call", callHelper);

        // Provide a minimal `Function.prototype` object that exposes the
        // same call helper so patterns like
        // `Function.prototype.call.bind(Object.prototype.hasOwnProperty)`
        // work as expected.
        var functionPrototype = new JsObject();
        functionPrototype.SetProperty("call", callHelper);
        if (realm.ObjectPrototype is not null)
        {
            functionPrototype.SetPrototype(realm.ObjectPrototype);
        }

        functionPrototype.SetProperty("constructor", functionConstructor);
        functionPrototype.SetHostedProperty("toString", FunctionPrototypeToString);
        functionPrototype.SetHostedProperty("valueOf", (thisValue, _) => thisValue);
        var thrower = new HostFunction((_, _) => throw ThrowTypeError(
            "Access to caller or arguments is not allowed", realm: realm))
        {
            IsConstructor = false, RealmState = realm
        };
        var poisonDescriptor = new PropertyDescriptor
        {
            Get = thrower, Set = thrower, Enumerable = false, Configurable = false
        };
        functionPrototype.DefineProperty("caller", poisonDescriptor);
        functionPrototype.DefineProperty("arguments", poisonDescriptor);

        var hasInstanceKey = $"@@symbol:{TypedAstSymbol.For("Symbol.hasInstance").GetHashCode()}";
        var hasInstance = new HostFunction((thisValue, args) =>
        {
            if (thisValue is not IJsPropertyAccessor)
            {
                throw ThrowTypeError("Function.prototype[@@hasInstance] called on non-object", null, realm);
            }

            var candidate = args.Count > 0 ? args[0] : Symbol.Undefined;
            if (candidate is not JsObject && candidate is not IJsObjectLike)
            {
                return false;
            }

            if (!JsOps.TryGetPropertyValue(thisValue, "prototype", out var protoVal) ||
                protoVal is not JsObject prototypeObject)
            {
                throw ThrowTypeError("Function has non-object prototype in instanceof check", null, realm);
            }

            var cursor = candidate switch
            {
                JsObject obj => obj.Prototype,
                IJsObjectLike objectLike => objectLike.Prototype,
                _ => null
            };

            while (cursor is not null)
            {
                if (ReferenceEquals(cursor, prototypeObject))
                {
                    return true;
                }

                cursor = cursor.Prototype;
            }

            return false;
        }) { RealmState = realm };
        functionPrototype.SetProperty(hasInstanceKey, hasInstance);
        realm.FunctionPrototype ??= functionPrototype;
        functionConstructor.SetProperty("prototype", functionPrototype);
        functionConstructor.Properties.SetPrototype(functionPrototype);

        return functionConstructor;

        object? FunctionPrototypeToString(object? thisValue, IReadOnlyList<object?> _)
        {
            // Provide a minimal native-code style representation for host and user functions.
            return thisValue switch
            {
                IJsCallable => "function() { [native code] }",
                _ => "function undefined() { [native code] }"
            };
        }

        static string ToFunctionArgumentString(object? value, EvaluationContext evalContext, RealmState realmState)
        {
            var primitive = JsOps.ToPrimitive(value, "string", evalContext);
            if (evalContext.IsThrow)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

            switch (primitive)
            {
                case null:
                    return "null";
                case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
                    return "undefined";
                case Symbol:
                case TypedAstSymbol:
                    throw ThrowTypeError("Cannot convert a Symbol value to a string", evalContext, realmState);
                case bool flag:
                    return flag ? "true" : "false";
                case string s:
                    return s;
                case JsBigInt bigInt:
                    return bigInt.Value.ToString(CultureInfo.InvariantCulture);
                case double d when double.IsNaN(d):
                    return "NaN";
                case double d when double.IsPositiveInfinity(d):
                    return "Infinity";
                case double d when double.IsNegativeInfinity(d):
                    return "-Infinity";
                case double d:
                    return d.ToString(CultureInfo.InvariantCulture);
            }

            return Convert.ToString(primitive, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        static bool ContainsHtmlCloseCommentWithoutLineTerminator(string text)
        {
            var index = text.IndexOf("-->", StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var current = index;
            while (current >= 0)
            {
                var hasLineTerminatorBefore = false;
                for (var i = current - 1; i >= 0; i--)
                {
                    if (text[i] is '\r' or '\n' or '\u2028' or '\u2029')
                    {
                        hasLineTerminatorBefore = true;
                        break;
                    }
                }

                if (!hasLineTerminatorBefore)
                {
                    return true;
                }

                current = text.IndexOf("-->", current + 3, StringComparison.Ordinal);
            }

            return false;
        }
    }
}
