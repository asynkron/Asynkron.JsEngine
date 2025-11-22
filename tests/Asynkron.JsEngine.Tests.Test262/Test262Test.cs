using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Test262Harness;
using System;

namespace Asynkron.JsEngine.Tests.Test262;

public abstract partial class Test262Test
{
    private static readonly List<JsEngine> _realmEngines = new();

    private static async Task<JsEngine> BuildTestExecutor(Test262File file)
    {
        var engine = new JsEngine
        {
            ExecutionTimeout = TimeSpan.FromSeconds(6)
        };

        if (file.Flags.Contains("raw"))
        {
            // nothing should be loaded
            return engine;
        }

        // Execute test harness files
        await ExecuteSource(engine, State.Sources["assert.js"]);
        await ExecuteSource(engine, State.Sources["sta.js"]);

        // Add print function
        engine.SetGlobalFunction("print", args =>
        {
            if (args.Count > 0)
            {
                var value = args[0];
                // Convert to string representation
                return value?.ToString() ?? "";
            }
            return "";
        });

        // Create $262 object for Test262 compatibility
        var obj262 = new JsObject
        {
            // evalScript function
            ["evalScript"] = new HostFunction(args =>
            {
                if (args.Count > 1)
                {
                    throw new Exception("only script parsing supported");
                }

                if (args.Count > 0 && args[0] is string script)
                {
                    return ExecuteSource(engine, script).GetAwaiter().GetResult();
                }

                return null;
            }),

            // createRealm function - not fully implemented but needed for compatibility
            ["createRealm"] = new HostFunction(args =>
            {
                // Create a fresh engine with its own intrinsics; expose its global
                // object so tests can access constructors like Array/Function.
                var realmEngine = new JsEngine();
                var realmGlobal = realmEngine.GlobalObject;
                realmGlobal["global"] = realmGlobal;
                _realmEngines.Add(realmEngine);
                return realmGlobal;
            }),

            // detachArrayBuffer function - placeholder implementation
            ["detachArrayBuffer"] = new HostFunction(args =>
            {
                // TODO: Implement proper ArrayBuffer detachment if needed
                return null;
            }),

            // gc function - triggers garbage collection
            ["gc"] = new HostFunction(args =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return null;
            })
        };

        engine.SetGlobalValue("$262", obj262);

        // Load includes
        var includes = file.Includes.ToArray();
        foreach (var include in includes)
        {
            await ExecuteSource(engine, State.Sources[include]);
        }

        if (file.Flags.Contains("async"))
        {
            await ExecuteSource(engine, State.Sources["doneprintHandle.js"]);
        }

        return engine;
    }

    private static async Task<object?> ExecuteSource(JsEngine engine, string source)
    {
        return await engine.Evaluate(source);
    }

    private static async Task ExecuteTest(JsEngine engine, Test262File file)
    {
        if (file.Type == ProgramType.Module)
        {
            // Module support - basic implementation
            // For now, we'll treat modules as regular scripts
            // TODO: Implement proper module support if needed
            await ExecuteSource(engine, file.Program);
        }
        else
        {
            await ExecuteSource(engine, file.Program);
        }
    }

    private partial bool ShouldThrow(Test262File testCase, bool strict)
    {
        return testCase.Negative;
    }
}
