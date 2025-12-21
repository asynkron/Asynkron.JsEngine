using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Function", PrototypeType = typeof(FunctionPrototype), Length = 1d, DisplayName = "Function")]
public sealed partial class FunctionConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ConstructFunctionBody(args, _constructor!);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.FunctionPrototype ??= Prototype;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
            ConstructFunctionBody(args,
                newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : constructor));
    }

    private JsValue ConstructFunctionBody(IReadOnlyList<JsValue> args, IJsCallable newTarget)
    {
        var engine = Realm.Engine;
        if (engine is null)
        {
            throw ThrowTypeError("Function constructor requires an engine context", realm: Realm);
        }

        var evalContext = Realm.CreateContext();
        var argCount = args.Count;
        var bodyValue = argCount > 0 ? args[argCount - 1].ToObject() : string.Empty;
        var parameterCount = Math.Max(argCount - 1, 0);

        var parameters = new string[parameterCount];
        for (var i = 0; i < parameterCount; i++)
        {
            var paramText = ToFunctionArgumentString(args[i].ToObject(), evalContext, Realm);
            parameters[i] = paramText;
        }

        var bodySource = ToFunctionArgumentString(bodyValue, evalContext, Realm);
        var paramList = string.Join(',', parameters);
        var hasDanglingClose = ContainsHtmlCloseCommentWithoutLineTerminator(paramList);
        if (hasDanglingClose)
        {
            throw ThrowSyntaxError("Invalid function parameter list", evalContext, Realm);
        }

        var functionSource = $"(function anonymous({paramList}\n) {{\n{bodySource}\n}})";

        var scriptGoalOptions = new JsEngineOptions { AllowImportMeta = false };

        ProgramNode program;
        try
        {
            program = engine.ParseProgram(functionSource, options: scriptGoalOptions);
        }
        catch (ParseException parseException)
        {
            var message = parseException.Message ?? "SyntaxError";
            throw new ThrowSignal(JsValue.FromObjectUnsafe(CreateSyntaxError(message, evalContext, Realm)));
        }

        var created = engine.ExecuteProgram(
            program,
            engine.GlobalEnvironment,
            CancellationToken.None);

        if (created is not IJsObjectLike objectLike)
        {
            return JsValue.FromObjectUnsafe(created);
        }

        var proto = ResolveConstructPrototype(newTarget, _constructor!, Realm);
        if (proto is not null)
        {
            objectLike.SetPrototype(proto);
        }

        return JsValue.FromObjectUnsafe(created);
    }

    private static string ToFunctionArgumentString(object? value, EvaluationContext evalContext, RealmState realmState)
    {
        var primitive = JsOps.ToPrimitive(value, ToPrimitiveHint.String, evalContext);
        if (evalContext.IsThrow)
        {
            throw new ThrowSignal(evalContext.FlowValue);
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
