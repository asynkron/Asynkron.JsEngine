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
        var functionPrototype = FunctionPrototype.CreatePrototype(realm);
        realm.FunctionPrototype ??= functionPrototype;

        HostFunction functionConstructor = null!;
        functionConstructor = new HostFunction((_, args) => FunctionConstructorBody(args, functionConstructor))
        {
            RealmState = realm
        };

        functionConstructor.SetInvokeWithContext((args, _, _, newTarget) =>
            FunctionConstructorBody(args, newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : functionConstructor));

        functionConstructor.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        functionConstructor.DefineProperty("name",
            new PropertyDescriptor { Value = "Function", Writable = false, Enumerable = false, Configurable = true });

        functionConstructor.DefineProperty("prototype",
            new PropertyDescriptor { Value = functionPrototype, Writable = false, Enumerable = false, Configurable = false });
        if (functionPrototype is IPropertyDefinitionHost definable &&
            definable.TryDefineProperty("constructor",
                new PropertyDescriptor
                {
                    Value = functionConstructor, Writable = true, Enumerable = false, Configurable = true
                }))
        {
            // property defined via descriptor
        }
        else
        {
            functionPrototype.SetProperty("constructor", functionConstructor);
        }

        functionConstructor.Properties.SetPrototype(functionPrototype);

        return functionConstructor;

        JsValue FunctionConstructorBody(IReadOnlyList<JsValue> args, IJsCallable newTarget)
        {
            var evalContext = realm.CreateContext();
            var argCount = args.Count;
            var bodyValue = argCount > 0 ? args[argCount - 1].ToObject() : string.Empty;
            var parameterCount = Math.Max(argCount - 1, 0);

            var parameters = new string[parameterCount];
            for (var i = 0; i < parameterCount; i++)
            {
                var paramText = ToFunctionArgumentString(args[i].ToObject(), evalContext, realm);
                parameters[i] = paramText;
            }

            var bodySource = ToFunctionArgumentString(bodyValue, evalContext, realm);
            var paramList = string.Join(",", parameters);
            var hasDanglingClose = ContainsHtmlCloseCommentWithoutLineTerminator(paramList);
            if (hasDanglingClose)
            {
                throw ThrowSyntaxError("Invalid function parameter list", evalContext, realm);
            }

            var functionSource = $"(function anonymous({paramList}\n) {{\n{bodySource}\n}})";

            var scriptGoalOptions = new JsEngineOptions
            {
                AllowImportMeta = false
            };

            ParsedProgram program;
            try
            {
                program = engine.ParseForExecution(functionSource, options: scriptGoalOptions);
            }
            catch (ParseException parseException)
            {
                var message = parseException.Message ?? "SyntaxError";
                throw new ThrowSignal(JsValue.FromObject(CreateSyntaxError(message, evalContext, realm)));
            }

            var created = engine.ExecuteProgram(
                program,
                engine.GlobalEnvironment,
                CancellationToken.None);

            if (created is IJsObjectLike objectLike)
            {
                var proto = ResolveConstructPrototype(newTarget, functionConstructor, realm);
                if (proto is not null)
                {
                    objectLike.SetPrototype(proto);
                }
            }

            return JsValue.FromObject(created);
        }
    }

    private static string ToFunctionArgumentString(object? value, EvaluationContext evalContext, RealmState realmState)
    {
        var primitive = JsOps.ToPrimitive(value, ToPrimitiveHint.String, evalContext);
        if (evalContext.IsThrow)
        {
            throw new ThrowSignal(JsValue.FromObject(evalContext.FlowValue));
        }

        return primitive switch
        {
            null => "null",
            Symbol sym when ReferenceEquals(sym, Symbol.Undefined) => "undefined",
            Symbol or TypedAstSymbol => throw ThrowTypeError("Cannot convert a Symbol value to a string", evalContext,
                realmState),
            bool flag => flag ? "true" : "false",
            string s => s,
            JsBigInt bigInt => bigInt.Value.ToString(CultureInfo.InvariantCulture),
            double.NaN => "NaN",
            double d when double.IsPositiveInfinity(d) => "Infinity",
            double d when double.IsNegativeInfinity(d) => "-Infinity",
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(primitive, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static bool ContainsHtmlCloseCommentWithoutLineTerminator(string text)
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
