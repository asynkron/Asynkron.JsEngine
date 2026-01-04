#region

using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Function", PrototypeType = typeof(FunctionPrototype), Length = 1d, DisplayName = "Function")]
public sealed partial class FunctionConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
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
        var bodyValue = argCount > 0 ? args[argCount - 1] : JsValue.Undefined;
        var parameterCount = Math.Max(argCount - 1, 0);

        var parameters = new string[parameterCount];
        for (var i = 0; i < parameterCount; i++)
        {
            var paramText = ToFunctionArgumentString(args[i], evalContext, Realm);
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
        return ParseAndExecuteDynamicFunction(functionSource, engine, evalContext, Realm, newTarget, _constructor!);
    }

    internal static JsValue ParseAndExecuteDynamicFunction(
        string functionSource,
        JsEngine engine,
        EvaluationContext evalContext,
        RealmState realm,
        IJsCallable newTarget,
        IJsCallable constructorFallback)
    {
        var scriptGoalOptions = new JsEngineOptions { AllowImportMeta = false };

        ProgramNode program;
        try
        {
            program = engine.ParseProgram(functionSource, options: scriptGoalOptions);
        }
        catch (ParseException parseException)
        {
            var message = parseException.Message ?? "SyntaxError";
            throw new ThrowSignal(CreateSyntaxError(message, evalContext, realm));
        }

        var created = engine.ExecuteProgram(
            program,
            engine.GlobalEnvironment,
            CancellationToken.None);

        if (created is not IJsObjectLike objectLike)
        {
            return JsValue.FromObjectUnsafe(created);
        }

        var proto = ResolveConstructPrototype(newTarget, constructorFallback, realm);
        if (proto is not null)
        {
            objectLike.SetPrototype(proto);
        }

        return JsValue.FromObjectUnsafe(created);
    }

    internal static string ToFunctionArgumentString(JsValue value, EvaluationContext evalContext, RealmState realmState)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "null",
            JsValueKind.Boolean => value.NumberValue != 0 ? "true" : "false",
            JsValueKind.Number => NumberToString(value.NumberValue),
            JsValueKind.String => value.ObjectValue as string ?? string.Empty,
            JsValueKind.Symbol => throw ThrowTypeError("Cannot convert a Symbol value to a string", evalContext,
                realmState),
            JsValueKind.BigInt => value.ObjectValue is JsBigInt bigInt
                ? bigInt.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            JsValueKind.Object => ToFunctionArgumentStringFromObject(JsValue.FromObjectUnsafe(value.ObjectValue),
                evalContext, realmState),
            _ => string.Empty
        };
    }

    internal static string NumberToString(double d)
    {
        if (double.IsNaN(d))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(d))
        {
            return "Infinity";
        }

        if (double.IsNegativeInfinity(d))
        {
            return "-Infinity";
        }

        return d.ToString(CultureInfo.InvariantCulture);
    }

    internal static string ToFunctionArgumentStringFromObject(JsValue value, EvaluationContext evalContext,
        RealmState realmState)
    {
        var primitive = JsOps.ToPrimitive(value, ToPrimitiveHint.String, evalContext);
        if (evalContext.IsThrow)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        return primitive.Kind switch
        {
            JsValueKind.Null => "null",
            JsValueKind.Undefined => "undefined",
            JsValueKind.Symbol => throw ThrowTypeError("Cannot convert a Symbol value to a string", evalContext,
                realmState),
            JsValueKind.Boolean => primitive.NumberValue != 0 ? "true" : "false",
            JsValueKind.String => primitive.AsString() ?? string.Empty,
            JsValueKind.BigInt => primitive.AsBigInt().Value.ToString(CultureInfo.InvariantCulture),
            JsValueKind.Number => NumberToString(primitive.NumberValue),
            _ => Convert.ToString(primitive.ObjectValue, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    internal static bool ContainsHtmlCloseCommentWithoutLineTerminator(string text)
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
                if (text[i] is not ('\r' or '\n' or '\u2028' or '\u2029'))
                {
                    continue;
                }

                hasLineTerminatorBefore = true;
                break;
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
