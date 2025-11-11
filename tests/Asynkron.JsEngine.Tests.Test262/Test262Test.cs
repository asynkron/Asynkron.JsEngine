using Asynkron.JsEngine;
using Test262Harness;

namespace Asynkron.JsEngine.Tests.Test262;

public abstract partial class Test262Test
{
    private static JsEngine BuildTestExecutor(Test262File file)
    {
        var engine = new JsEngine();

        if (file.Flags.Contains("raw"))
        {
            // nothing should be loaded
            return engine;
        }

        // Execute test harness files
        ExecuteSource(engine, State.Sources["assert.js"]);
        ExecuteSource(engine, State.Sources["sta.js"]);

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
                    return ExecuteSource(engine, script);
                }
                
                return null;
            }),

            // createRealm function - not fully implemented but needed for compatibility
            ["createRealm"] = new HostFunction(args =>
            {
                // Return a new global-like object
                var realmGlobal = new JsObject();
                realmGlobal["global"] = realmGlobal;
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

        engine.SetGlobal("$262", obj262);

        // Load includes
        foreach (var include in file.Includes)
        {
            ExecuteSource(engine, State.Sources[include]);
        }

        if (file.Flags.Contains("async"))
        {
            ExecuteSource(engine, State.Sources["doneprintHandle.js"]);
        }

        return engine;
    }

    private static object? ExecuteSource(JsEngine engine, string source)
    {
        try
        {
            var task = engine.Evaluate(source);
            task.Wait();
            return task.Result;
        }
        catch (AggregateException ae)
        {
            // Unwrap the AggregateException to get the actual JavaScript error
            if (ae.InnerException != null)
            {
                throw ae.InnerException;
            }
            throw;
        }
    }

    private static void ExecuteTest(JsEngine engine, Test262File file)
    {
        if (file.Type == ProgramType.Module)
        {
            // Module support - basic implementation
            // For now, we'll treat modules as regular scripts
            // TODO: Implement proper module support if needed
            ExecuteSource(engine, file.Program);
        }
        else
        {
            ExecuteSource(engine, file.Program);
        }
    }

    private partial bool ShouldThrow(Test262File testCase, bool strict)
    {
        return testCase.Negative;
    }
}
