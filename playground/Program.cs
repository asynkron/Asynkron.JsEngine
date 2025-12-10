using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        var funcObj = await engine.Evaluate("""
let ref = function * BindingIdentifier() {
  return BindingIdentifier;
};
ref;
""");

        Console.WriteLine(funcObj?.GetType().FullName);
        var isStrictField = funcObj?.GetType().GetField("_isLexicallyStrict",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Console.WriteLine($"lexically strict: {isStrictField?.GetValue(funcObj)}");

        var closureField = funcObj?.GetType().GetField("_closure",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var closure = closureField?.GetValue(funcObj);
        Console.WriteLine($"closure strict: {(closure as Asynkron.JsEngine.JsEnvironment)?.IsStrict}");
        if (closure is Asynkron.JsEngine.JsEnvironment env)
        {
            var valuesField = typeof(Asynkron.JsEngine.JsEnvironment)
                .GetField("_values", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (valuesField?.GetValue(env) is System.Collections.IDictionary map)
            {
                foreach (System.Collections.DictionaryEntry kvp in map)
                {
                    var keyName = kvp.Key?.GetType().GetProperty("Name")?.GetValue(kvp.Key) as string;
                    if (keyName == "BindingIdentifier" && kvp.Value is not null)
                    {
                        var bindingType = kvp.Value.GetType();
                        Console.WriteLine($"binding type: {bindingType.FullName}");
                        var constProp = bindingType.GetProperty("IsConst");
                        var immutableProp = bindingType.GetProperty("IsImmutableBinding");
                        Console.WriteLine($"IsConst={constProp?.GetValue(kvp.Value)} IsImmutable={immutableProp?.GetValue(kvp.Value)}");
                    }
                }
            }
        }

        var functionField = funcObj?.GetType().GetField("_function",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var realmField = funcObj?.GetType().GetField("_realmState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var homeObjectField = funcObj?.GetType().GetField("_homeObject",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var capturedField = funcObj?.GetType().GetField("_capturedPrivateNameScopes",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var privateScopeField = funcObj?.GetType().GetField("_privateNameScope",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var instanceType = Type.GetType("Asynkron.JsEngine.Ast.TypedAstEvaluator+TypedGeneratorInstance, Asynkron.JsEngine");
        if (instanceType is not null)
        {
            var ctor = instanceType.GetConstructors(System.Reflection.BindingFlags.Instance |
                                                    System.Reflection.BindingFlags.NonPublic |
                                                    System.Reflection.BindingFlags.Public)[0];
            var instance = ctor.Invoke(new[]
            {
                functionField!.GetValue(funcObj),
                closure,
                Array.Empty<object?>(),
                null,
                funcObj,
                realmField!.GetValue(funcObj),
                isStrictField!.GetValue(funcObj),
                homeObjectField!.GetValue(funcObj),
                privateScopeField!.GetValue(funcObj),
                capturedField!.GetValue(funcObj)!
            });
            var strictField = instanceType.GetField("_isStrict",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Console.WriteLine($"reflected instance _isStrict: {strictField?.GetValue(instance)}");

            var ensureEnv = instanceType.GetMethod("EnsureExecutionEnvironment",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var execEnv = ensureEnv?.Invoke(instance, Array.Empty<object?>()) as JsEnvironment;
            var ensureCtx = instanceType.GetMethod("EnsureEvaluationContext",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var ctx = ensureCtx?.Invoke(instance, Array.Empty<object?>()) as EvaluationContext;
            var getFuncScope = typeof(JsEnvironment).GetMethod("GetFunctionScope",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var funcScope = getFuncScope?.Invoke(execEnv, null) as JsEnvironment;
            Console.WriteLine($"execEnv strict={execEnv?.IsStrict} funcScopeStrict={funcScope?.IsStrict}");
            Console.WriteLine($"context strict scope={ctx?.CurrentScope.IsStrict}");
        }
    }
}
