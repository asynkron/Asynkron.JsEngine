#region

using System;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncFunction constructor for creating async functions dynamically
/// </summary>
[JsConstructor("AsyncFunction", PrototypeType = typeof(AsyncFunctionPrototype), Length = 1d, DisplayName = "AsyncFunction")]
public sealed partial class AsyncFunctionConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ConstructAsyncFunctionBody(args, _constructor ?? throw new InvalidOperationException(
            "AsyncFunction constructor not initialized"));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.AsyncFunctionPrototype ??= Prototype as JsObject;
        Realm.AsyncFunctionConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
            ConstructAsyncFunctionBody(args,
                newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : constructor));

        if (Realm.Engine?.GlobalObject.TryGetProperty("Function", out var functionCtorValue) == true &&
            functionCtorValue.TryGetObject<IJsObjectLike>(out var functionCtor))
        {
            constructor.Properties.SetPrototype(functionCtor);
        }

        if (Prototype is IJsObjectLike protoObj && protoObj.GetOwnPropertyDescriptor("constructor") is null)
        {
            protoObj.DefineProperty("constructor",
                new PropertyDescriptor
                {
                    Value = constructor,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
        }
    }

    private JsValue ConstructAsyncFunctionBody(IReadOnlyList<JsValue> args, IJsCallable newTarget)
    {
        var engine = Realm.Engine;
        if (engine is null)
        {
            throw ThrowTypeError("AsyncFunction constructor requires an engine context", realm: Realm);
        }

        var evalContext = Realm.CreateContext();
        var argCount = args.Count;
        var bodyValue = argCount > 0 ? args[argCount - 1] : JsValue.Undefined;
        var parameterCount = Math.Max(argCount - 1, 0);

        var parameters = new string[parameterCount];
        for (var i = 0; i < parameterCount; i++)
        {
            parameters[i] = FunctionConstructor.ToFunctionArgumentString(args[i], evalContext, Realm);
        }

        var bodySource = FunctionConstructor.ToFunctionArgumentString(bodyValue, evalContext, Realm);
        var paramList = string.Join(',', parameters);
        var hasDanglingClose = FunctionConstructor.ContainsHtmlCloseCommentWithoutLineTerminator(paramList);
        if (hasDanglingClose)
        {
            throw ThrowSyntaxError("Invalid function parameter list", evalContext, Realm);
        }

        var functionSource = $"(async function anonymous({paramList}\n) {{\n{bodySource}\n}})";

        var scriptGoalOptions = new JsEngineOptions { AllowImportMeta = false };

        ProgramNode program;
        try
        {
            program = engine.ParseProgram(functionSource, options: scriptGoalOptions);
        }
        catch (ParseException parseException)
        {
            var message = parseException.Message ?? "SyntaxError";
            throw new ThrowSignal(CreateSyntaxError(message, evalContext, Realm));
        }

        var created = engine.ExecuteProgram(
            program,
            engine.GlobalEnvironment,
            CancellationToken.None);

        if (created is not IJsObjectLike objectLike)
        {
            return JsValue.FromObjectUnsafe(created);
        }

        var proto = ResolveConstructPrototype(newTarget, _constructor ?? newTarget, Realm);
        if (proto is not null)
        {
            objectLike.SetPrototype(proto);
        }

        return JsValue.FromObjectUnsafe(created);
    }
}
