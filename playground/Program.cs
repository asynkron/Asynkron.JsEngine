using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();

        try
        {
            var parsed = engine.ParseWithTransformationSteps("""
switch (0) { default: async function x() {} }
x;
""");
            if (parsed.constantFolded.Body[0] is SwitchStatement switchStmt &&
                switchStmt.Cases.Length > 0)
            {
                var firstStmt = switchStmt.Cases[0].Body.Statements.FirstOrDefault();
                Console.WriteLine($"first stmt type: {firstStmt?.GetType().Name}");
                if (firstStmt is FunctionDeclaration fd)
                {
                    Console.WriteLine($"function async={fd.Function.IsAsync} generator={fd.Function.IsGenerator}");
                }
            }
            if (parsed.cpsTransformed.Body[0] is SwitchStatement cpsSwitch &&
                cpsSwitch.Cases.Length > 0)
            {
                var firstStmt = cpsSwitch.Cases[0].Body.Statements.FirstOrDefault();
                Console.WriteLine($"[cps] first stmt type: {firstStmt?.GetType().Name}");
                if (firstStmt is FunctionDeclaration fd)
                {
                    Console.WriteLine($"[cps] function async={fd.Function.IsAsync} generator={fd.Function.IsGenerator}");
                }
            }

            var value = await engine.Evaluate("""
switch (0) { default: async function x() {} }
x;
""");
            Console.WriteLine($"value: {value} ({value?.GetType().FullName})");
            Console.WriteLine($"global has x: {engine.GlobalObject.TryGetValue("x", out var globalX)} value={globalX}");
            var globalEnvProp = typeof(JsEngine).GetProperty("GlobalEnvironment",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (globalEnvProp?.GetValue(engine) is JsEnvironment globalEnv)
            {
                var valuesField = typeof(JsEnvironment).GetField("_values",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (valuesField?.GetValue(globalEnv) is System.Collections.IDictionary map &&
                    map.Contains(Symbol.Intern("x")))
                {
                    var binding = map[Symbol.Intern("x")];
                    var bindingType = binding?.GetType();
                    Console.WriteLine($"global binding type: {bindingType?.FullName}");
                    if (bindingType is not null)
                    {
                        foreach (var prop in bindingType.GetProperties(
                                     System.Reflection.BindingFlags.Instance |
                                     System.Reflection.BindingFlags.Public |
                                     System.Reflection.BindingFlags.NonPublic))
                        {
                            if (prop.Name is "IsConst" or "IsLexical" or "CanDelete" or "BlocksFunctionScopeOverride" ||
                                prop.Name is "IsGlobalConstant" or "IsImmutableBinding")
                            {
                                Console.WriteLine($"{prop.Name}: {prop.GetValue(binding)}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"threw: {ex}");
        }
    }
}
